namespace FSH.CLI.Infrastructure;

/// <summary>
/// Thin wrapper over the <c>git</c> commands <c>fsh upgrade</c> needs, always scoped to an
/// explicit repository directory.
/// </summary>
internal static class GitRunner
{
    internal static async Task<(bool ok, string output)> RunAsync(
        string repository, string arguments, CancellationToken cancellationToken, bool trimOutput = true)
    {
        var result = await ProcessRunner
            .CaptureWithErrorAsync("git", arguments, repository, trimOutput, cancellationToken)
            .ConfigureAwait(false);

        return (result.exitCode == 0, string.IsNullOrEmpty(result.output) ? result.error : result.output);
    }

    internal static async Task<bool> IsRepositoryAsync(string directory, CancellationToken cancellationToken)
    {
        (bool ok, string output) = await RunAsync(directory, "rev-parse --is-inside-work-tree", cancellationToken)
            .ConfigureAwait(false);

        return ok && output.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the working tree has no staged or unstaged changes.</summary>
    internal static async Task<bool> IsCleanAsync(string repository, CancellationToken cancellationToken)
    {
        (bool ok, string output) = await RunAsync(repository, "status --porcelain", cancellationToken)
            .ConfigureAwait(false);

        return ok && string.IsNullOrWhiteSpace(output);
    }

    internal static async Task<string?> CurrentBranchAsync(string repository, CancellationToken cancellationToken)
    {
        (bool ok, string output) = await RunAsync(repository, "rev-parse --abbrev-ref HEAD", cancellationToken)
            .ConfigureAwait(false);

        return ok ? output.Trim() : null;
    }

    /// <summary>
    /// Finds the commit an upgrade should treat as the template's previous state.
    /// </summary>
    /// <remarks>
    /// The most recent <c>fsh upgrade</c> commit if there is one, else the pristine scaffold commit
    /// <c>fsh new</c> created.
    ///
    /// Using the newest upgrade rather than always the original scaffold is what keeps repeat
    /// upgrades small. Rooted at the original scaffold, every file a previous upgrade introduced
    /// looks newly added again and collides with the copy already in the branch - on a project that
    /// had been upgraded once, that was the difference between 130 files with 12 conflicts and 17
    /// files with one.
    /// </remarks>
    internal static async Task<string?> FindUpgradeBaselineAsync(string repository, CancellationToken cancellationToken)
    {
        string? latestUpgrade = await FindCommitAsync(
            repository,
            subject => subject.StartsWith(FshConstants.UpgradeCommitMessagePrefix, StringComparison.Ordinal)
                       && subject.EndsWith(FshConstants.UpgradeCommitMessageSuffix, StringComparison.Ordinal),
            newest: true,
            cancellationToken).ConfigureAwait(false);

        return latestUpgrade
            ?? await FindCommitAsync(
                repository,
                subject => subject.Equals(FshConstants.InitialCommitMessage, StringComparison.Ordinal),
                newest: false,
                cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The oldest or newest commit whose subject matches, searching all refs so an upgrade commit
    /// still counts when it lives only on a branch that was merged.
    /// </summary>
    private static async Task<string?> FindCommitAsync(
        string repository, Func<string, bool> matches, bool newest, CancellationToken cancellationToken)
    {
        // "<hash> <subject>": a commit hash never contains a space, so one split is unambiguous.
        // %x20 rather than a literal space: the argument string is split on whitespace before it
        // reaches git, so "--format=%H %s" would arrive as two separate arguments.
        // --all so a commit reachable only from the upgrade branch is still found.
        string order = newest ? string.Empty : " --reverse";
        (bool ok, string output) = await RunAsync(
            repository, $"log --all{order} --format=%H%x20%s", cancellationToken).ConfigureAwait(false);

        if (!ok) return null;

        foreach (string line in output.Split('\n'))
        {
            string[] parts = line.Trim().Split(' ', 2);
            if (parts.Length == 2 && matches(parts[1].Trim()))
                return parts[0];
        }

        return null;
    }

    /// <summary>Paths tracked at a given commit.</summary>
    internal static async Task<IReadOnlyList<string>> ListTreeAsync(
        string repository, string reference, CancellationToken cancellationToken)
    {
        (bool ok, string output) = await RunAsync(
            repository, $"ls-tree -r --name-only {reference}", cancellationToken).ConfigureAwait(false);

        return ok
            ? [.. output.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0)]
            : [];
    }

    /// <summary>Contents of a single file at a given commit, or null when it did not exist.</summary>
    internal static async Task<string?> ShowFileAsync(
        string repository, string reference, string path, CancellationToken cancellationToken)
    {
        // Not trimmed: this content is written straight back to disk, and dropping the trailing
        // newline would show the restored file as modified in every subsequent diff.
        (bool ok, string output) = await RunAsync(
            repository, $"show {reference}:\"{path}\"", cancellationToken, trimOutput: false)
            .ConfigureAwait(false);

        return ok ? output : null;
    }
}
