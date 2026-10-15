using Finbuckle.MultiTenant.Abstractions;
using FSH.Framework.Persistence.Context;
using FSH.Framework.Shared.Multitenancy;
using FSH.Framework.Shared.Persistence;
using FSH.Modules.Files.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FSH.Modules.Files.Data;

public sealed class FilesDbContext : BaseDbContext
{
    public const string Schema = "files";

    public FilesDbContext(
        IMultiTenantContextAccessor<AppTenantInfo> multiTenantContextAccessor,
        DbContextOptions<FilesDbContext> options,
        IOptions<DatabaseOptions> settings,
        IHostEnvironment environment) : base(multiTenantContextAccessor, options, settings, environment) { }

    public DbSet<FileAsset> FileAssets => Set<FileAsset>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FilesDbContext).Assembly);

        if (Database.IsSqlServer())
        {
            ConfigureSqlServerLegacyKeyIndex(modelBuilder);
        }

        // base.OnModelCreating runs LAST so BaseDbContext's auto-apply sees
        // fully-configured entities (including HasMany child types).
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Keeps IX_FileAsset_LegacyKey legal on SQL Server, whose filtered indexes reject <c>LIKE</c>.
    /// </summary>
    /// <remarks>
    /// Only the equality half of the filter survives, so the index holds every Available public file
    /// rather than just the legacy-keyed ones and never empties. The legacy-key scan still seeks it and
    /// applies the <c>LIKE</c> predicates as a residual — correct, just not the empty-index probe
    /// PostgreSQL gets.
    /// </remarks>
    private static void ConfigureSqlServerLegacyKeyIndex(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<FileAsset>()
            .HasIndex(x => x.Id)
            .HasFilter("[Visibility] = 0 AND [Status] = 1");
}
