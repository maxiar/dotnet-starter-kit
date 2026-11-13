using Spectre.Console;

namespace FSH.CLI.Infrastructure;

/// <summary>
/// Installs the FSH <c>dotnet new</c> template, honouring local-path / version / source
/// overrides. Shared by <c>fsh new</c> and <c>fsh framework swap</c> so both resolve the
/// template the same way.
/// </summary>
internal static class TemplateInstaller
{
    /// <summary>
    /// Makes sure a template is installed, honouring any explicit source/version override.
    /// </summary>
    /// <remarks>
    /// Without an override this keeps the historical behaviour: any installed FSH template is
    /// accepted as-is. That is deliberately sticky, and it is also why the overrides exist —
    /// a contributor working on a fork, or anyone whose installed template has gone stale,
    /// otherwise has no way to make `fsh new` use anything else.
    ///
    /// Exit codes are ignored throughout because `dotnet new` returns non-zero for unrelated
    /// workload warnings; success is confirmed by re-listing the templates instead.
    /// </remarks>
    internal static async Task<bool> EnsureInstalledAsync(
        string? templatePath, string? templateVersion, string? templateSource, bool refresh,
        CancellationToken cancellationToken)
    {
        string? path = FromSettingOrEnvironment(templatePath, FshConstants.TemplatePathEnvVar);
        string? version = FromSettingOrEnvironment(templateVersion, FshConstants.TemplateVersionEnvVar);
        string? source = FromSettingOrEnvironment(templateSource, FshConstants.TemplateSourceEnvVar);
        bool overridden = path is not null || version is not null || source is not null || refresh;

        if (!overridden)
        {
            (_, string listOutput) = await ProcessRunner.CaptureAsync(
                "dotnet", $"new list {FshConstants.TemplateShortName}",
                cancellationToken).ConfigureAwait(false);

            bool installed = listOutput.Contains(FshConstants.TemplateShortName, StringComparison.OrdinalIgnoreCase)
                && listOutput.Contains("FullStackHero", StringComparison.OrdinalIgnoreCase);

            if (installed) return true;

            AnsiConsole.MarkupLine($"[{FshConstants.WarningColor}]FSH template not found. Installing...[/]");
        }

        string target = FshConstants.TemplatePackageId;
        string? checkout = null;
        if (path is not null)
        {
            string resolved = ResolveTemplatePath(path);
            if (IsCheckout(resolved))
            {
                checkout = resolved;
                resolved = await SnapshotCheckoutAsync(resolved, cancellationToken).ConfigureAwait(false);
            }

            target = $"\"{resolved}\"";
        }
        else if (version is not null)
        {
            target = $"{FshConstants.TemplatePackageId}::{version}";
        }

        string arguments = $"new install {target} --force"
            + (source is not null ? $" --add-source \"{source}\"" : string.Empty);

        if (overridden)
        {
            AnsiConsole.MarkupLine($"[{FshConstants.DimColor}]Installing template: {target.EscapeMarkup()}[/]");

            // Uninstall first. Two packages that share the template identity
            // "FullStackHero.NET.StarterKit" make the template engine throw
            // ("Sequence contains more than one matching element") on the next `dotnet new fsh`,
            // and installing from a new source without removing the old one is precisely how
            // that state arises. Both forms are removed: the NuGet id, and the path we are
            // about to install (re-installing the same folder otherwise duplicates it).
            // A checkout installed directly by an older CLI is removed too: it would otherwise sit
            // next to its own snapshot under the same identity.
            string[] uninstallTargets = checkout is null
                ? [FshConstants.TemplatePackageId, target]
                : [FshConstants.TemplatePackageId, target, $"\"{checkout}\""];

            foreach (string uninstallTarget in uninstallTargets)
            {
                await ProcessRunner.CaptureAsync(
                    "dotnet", $"new uninstall {uninstallTarget}", cancellationToken).ConfigureAwait(false);
            }
        }

        await ProcessRunner.RunAsync("dotnet", arguments, cancellationToken: cancellationToken).ConfigureAwait(false);

        // Verify by re-listing rather than trusting the exit code (see remarks).
        (_, string verifyOutput) = await ProcessRunner.CaptureAsync(
            "dotnet", $"new list {FshConstants.TemplateShortName}",
            cancellationToken).ConfigureAwait(false);

        bool nowInstalled = verifyOutput.Contains("FullStackHero", StringComparison.OrdinalIgnoreCase);
        if (!nowInstalled)
            AnsiConsole.MarkupLine($"[{FshConstants.ErrorColor}]Failed to install template. Run manually:[/] dotnet {arguments.EscapeMarkup()}");

        return nowInstalled;
    }

    /// <summary>
    /// Resolves what to hand <c>dotnet new install</c> for a local template path.
    /// </summary>
    /// <remarks>
    /// Accepts all three shapes people reasonably pass: a .nupkg file, a starter-kit checkout
    /// (identified by its .template.config), or a folder of packed nupkgs — in which case the
    /// newest matching package is chosen, since "-o ./nupkgs" is exactly where `dotnet pack`
    /// puts it and installing a bare folder of packages otherwise fails with a confusing
    /// "no templates found in package".
    /// </remarks>
    internal static string ResolveTemplatePath(string path)
    {
        string full = Path.GetFullPath(path);

        if (File.Exists(full))
            return full;

        if (Directory.Exists(full))
        {
            if (Directory.Exists(Path.Combine(full, ".template.config")))
                return full;

            string? newest = Directory
                .EnumerateFiles(full, $"{FshConstants.TemplatePackageId}*.nupkg")
                .MaxBy(File.GetLastWriteTimeUtc);

            if (newest is not null)
                return newest;
        }

        return full;
    }

    private static bool IsCheckout(string path) =>
        Directory.Exists(Path.Combine(path, ".template.config"));

    /// <summary>
    /// Copies a starter-kit checkout's files - tracked, plus untracked ones git does not ignore - to
    /// a stable folder, and returns it; the checkout itself when it is not a git work tree.
    /// </summary>
    /// <remarks>
    /// The template engine reads the folder it is given, ignored files included: build output
    /// (<c>bin\Debug/</c> folders that macOS and Linux create with a literal backslash in the name,
    /// whose BuildHost DLLs land at the project root), <c>artifacts/</c> nupkgs, <c>.DS_Store</c>, local
    /// <c>.env</c> files. The template's own exclude list cannot keep up with every machine's junk;
    /// <c>.gitignore</c> already does. Copying from the working tree rather than <c>git archive</c>
    /// keeps uncommitted edits, which is what a contributor scaffolding from a checkout expects.
    ///
    /// The snapshot lives under <c>~/.fsh</c>, not a temp folder: <c>dotnet new install</c> registers
    /// the folder, and every later <c>dotnet new fsh</c> reads from it.
    /// </remarks>
    private static async Task<string> SnapshotCheckoutAsync(string checkout, CancellationToken cancellationToken)
    {
        (bool ok, string listing) = await GitRunner
            .RunAsync(checkout, "ls-files -z --cached --others --exclude-standard", cancellationToken, trimOutput: false)
            .ConfigureAwait(false);

        if (!ok || listing.Length == 0) return checkout;

        string snapshot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fsh", "template-snapshot");

        if (Directory.Exists(snapshot)) Directory.Delete(snapshot, recursive: true);

        int copied = 0;
        foreach (string relative in listing.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            string source = Path.Combine(checkout, relative);

            // Tracked but deleted in the working tree, or a submodule entry.
            if (!File.Exists(source)) continue;

            string destination = Path.Combine(snapshot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
            copied++;
        }

        AnsiConsole.MarkupLine(
            $"[{FshConstants.DimColor}]Template snapshot: {copied} files from {checkout.EscapeMarkup()} (git-ignored files left out)[/]");

        return snapshot;
    }

    /// <summary>Setting, else environment variable, else <see langword="null"/>.</summary>
    internal static string? FromSettingOrEnvironment(string? value, string environmentVariable)
    {
        if (!string.IsNullOrWhiteSpace(value)) return value;

        string? fromEnvironment = Environment.GetEnvironmentVariable(environmentVariable);
        return string.IsNullOrWhiteSpace(fromEnvironment) ? null : fromEnvironment;
    }
}
