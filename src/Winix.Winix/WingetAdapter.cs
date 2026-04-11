#nullable enable

namespace Winix.Winix;

/// <summary>
/// Package manager adapter for <c>winget</c> (Windows Package Manager).
/// Wraps winget CLI commands for install, upgrade, uninstall, and version detection.
/// </summary>
public sealed class WingetAdapter : IPackageManagerAdapter
{
    private readonly Func<string, string[], Task<ProcessResult>> _runAsync;

    /// <inheritdoc/>
    public string Name => "winget";

    /// <summary>
    /// Initialises a new <see cref="WingetAdapter"/> that calls the real winget process.
    /// </summary>
    public WingetAdapter() : this(ProcessHelper.RunAsync)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="WingetAdapter"/> with an injectable process runner.
    /// </summary>
    /// <param name="runAsync">
    /// Delegate used to invoke external processes. Injected in tests to avoid spawning
    /// the real winget binary and to allow argument verification.
    /// </param>
    public WingetAdapter(Func<string, string[], Task<ProcessResult>> runAsync)
    {
        _runAsync = runAsync;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Uses a fast PATH probe — does not run a full winget operation.
    /// </remarks>
    public bool IsAvailable()
    {
        return ProcessHelper.IsOnPath("winget");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Runs <c>winget list --id &lt;packageId&gt; --exact</c>. Returns <see langword="true"/>
    /// when the exit code is 0; any non-zero exit code (including "not found") returns
    /// <see langword="false"/>.
    /// </remarks>
    public async Task<bool> IsInstalled(string packageId)
    {
        ProcessResult result = await _runAsync(
            "winget",
            new[] { "list", "--id", packageId, "--exact" }).ConfigureAwait(false);

        return result.ExitCode == 0;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Runs <c>winget list --id &lt;packageId&gt; --exact</c> and parses the version
    /// from the tabular output. Returns <see langword="null"/> when the package is not
    /// installed (non-zero exit code) or the version cannot be parsed from the output.
    /// </remarks>
    public async Task<string?> GetInstalledVersion(string packageId)
    {
        ProcessResult result = await _runAsync(
            "winget",
            new[] { "list", "--id", packageId, "--exact" }).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return null;
        }

        return ParseVersionFromListOutput(result.Stdout, packageId);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Runs <c>winget install --id &lt;packageId&gt; --exact --accept-source-agreements</c>.
    /// </remarks>
    public Task<ProcessResult> Install(string packageId)
    {
        return _runAsync(
            "winget",
            new[] { "install", "--id", packageId, "--exact", "--accept-source-agreements" });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Runs <c>winget upgrade --id &lt;packageId&gt; --exact --accept-source-agreements</c>.
    /// </remarks>
    public Task<ProcessResult> Update(string packageId)
    {
        return _runAsync(
            "winget",
            new[] { "upgrade", "--id", packageId, "--exact", "--accept-source-agreements" });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Runs <c>winget uninstall --id &lt;packageId&gt; --exact</c>.
    /// </remarks>
    public Task<ProcessResult> Uninstall(string packageId)
    {
        return _runAsync(
            "winget",
            new[] { "uninstall", "--id", packageId, "--exact" });
    }

    /// <summary>
    /// Parses the installed version string from <c>winget list</c> output.
    /// </summary>
    /// <param name="stdout">
    /// The stdout text from <c>winget list --id &lt;packageId&gt; --exact</c>.
    /// Expected format: a header line, a dashes separator, then rows of
    /// <c>Name   Id   Version</c> columns separated by whitespace.
    /// </param>
    /// <param name="packageId">
    /// The package ID to locate in the output. Matched case-insensitively.
    /// </param>
    /// <returns>
    /// The version string (last whitespace-separated token on the matching row),
    /// or <see langword="null"/> when no matching row is found or the row has
    /// fewer than 3 tokens.
    /// </returns>
    internal static string? ParseVersionFromListOutput(string stdout, string packageId)
    {
        string[] lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (string line in lines)
        {
            string trimmed = line.Trim();

            if (!trimmed.Contains(packageId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 3)
            {
                // Version is the last whitespace-separated token on the data row.
                return parts[parts.Length - 1];
            }
        }

        return null;
    }
}
