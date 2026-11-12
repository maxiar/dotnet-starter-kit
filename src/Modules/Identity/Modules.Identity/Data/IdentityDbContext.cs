using Finbuckle.MultiTenant.Abstractions;
using Finbuckle.MultiTenant.Identity.EntityFrameworkCore;
using FSH.Framework.Persistence.Providers;
using FSH.Framework.Persistence;
using FSH.Framework.Shared.Multitenancy;
using FSH.Framework.Shared.Persistence;
using FSH.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FSH.Modules.Identity.Data;

public class IdentityDbContext : MultiTenantIdentityDbContext<FshUser,
    FshRole,
    string,
    IdentityUserClaim<string>,
    IdentityUserRole<string>,
    IdentityUserLogin<string>,
    FshRoleClaim,
    IdentityUserToken<string>,
    IdentityUserPasskey<string>>
{
    private readonly DatabaseOptions _settings;
    private new AppTenantInfo TenantInfo { get; set; }
    private readonly IHostEnvironment _environment;
    public DbSet<PasswordHistory> PasswordHistories => Set<PasswordHistory>();

    public DbSet<UserSession> UserSessions => Set<UserSession>();

    public DbSet<Group> Groups => Set<Group>();

    public DbSet<GroupRole> GroupRoles => Set<GroupRole>();

    public DbSet<UserGroup> UserGroups => Set<UserGroup>();

    public DbSet<ImpersonationGrant> ImpersonationGrants => Set<ImpersonationGrant>();

    public IdentityDbContext(
        IMultiTenantContextAccessor<AppTenantInfo> multiTenantContextAccessor,
        DbContextOptions<IdentityDbContext> options,
        IOptions<DatabaseOptions> settings,
        IHostEnvironment environment) : base(multiTenantContextAccessor, options)
    {
        ArgumentNullException.ThrowIfNull(multiTenantContextAccessor);
        ArgumentNullException.ThrowIfNull(settings);

        _environment = environment;
        _settings = settings.Value;
        TenantInfo = multiTenantContextAccessor.MultiTenantContext.TenantInfo!;
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);

        if (Database.IsSqlServer())
        {
            ConfigureSqlServerRoleClaimKeys(builder);
        }

        // The outbox/inbox tables are framework infrastructure, owned by EventingDbContext (issue #1349).

        // Default-on tenant isolation: non-IGlobalEntity entities get IsMultiTenant() automatically (ImpersonationGrant opts out).
        // Identity tables are already IsMultiTenant in IdentityConfigurations.cs; auto-apply detects that annotation and skips them.
        builder.ApplyTenantIsolationByDefault();
    }

    /// <summary>
    /// Keeps the unique index on <c>RoleClaims (RoleId, ClaimType, ClaimValue)</c> legal on SQL Server.
    /// </summary>
    /// <remarks>
    /// Identity maps both claim columns to <c>nvarchar(max)</c>, which SQL Server cannot use as index
    /// keys. Bounding them keeps the key within SQL Server's 1700-byte limit for a nonclustered index:
    /// RoleId nvarchar(450) = 900 bytes, ClaimType nvarchar(128) = 256, ClaimValue nvarchar(256) = 512.
    /// Permission claims are far shorter than either bound. PostgreSQL keeps unbounded text.
    /// </remarks>
    private static void ConfigureSqlServerRoleClaimKeys(ModelBuilder builder) =>
        builder.Entity<FshRoleClaim>(roleClaim =>
        {
            roleClaim.Property(rc => rc.ClaimType).HasMaxLength(128);
            roleClaim.Property(rc => rc.ClaimValue).HasMaxLength(256);
        });

    /// <summary>
    /// This context does not derive from BaseDbContext, so it registers the framework's provider
    /// conventions itself — without them the portable column intent is never resolved.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.AddHeroProviderConventions(DbProviderResolver.FromEfProviderName(Database.ProviderName));
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!string.IsNullOrWhiteSpace(TenantInfo?.ConnectionString))
        {
            optionsBuilder.ConfigureHeroDatabase(
                _settings.Provider,
                TenantInfo.ConnectionString,
                _settings.MigrationsAssembly,
                _environment.IsDevelopment());
        }
    }
}