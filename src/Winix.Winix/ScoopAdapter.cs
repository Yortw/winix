#nullable enable

namespace Winix.Winix;

/// <summary>
/// Package manager adapter for <c>scoop</c> (Windows Scoop).
/// Wraps scoop CLI commands for install, update, uninstall, version detection,
/// and automatic registration of the <c>winix</c> bucket.
/// </summary>
public sealed class ScoopAdapter : IPackageManagerAdapter
{
    private readonly Func<string, string[], Task<ProcessResult>> _runAsync;

    private const string BucketName = "winix";
    private const string BucketUrl = "https://github.com/Yortw/winix";

    /// <inheritdoc/>
    public string Name => "scoop";

    /// <summary>
    /// Initialises a new <see cref="ScoopAdapter"/> that calls the real scoop process.
    /// </summary>
    public ScoopAdapter() : this(ProcessHelper.RunAsync)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="ScoopAdapter"/> with an injectable process runner.
    /// </summary>
    /// <param name="runAsync">
    /// Delegate used to invoke external processes. Injected in tests to avoid spawning
    /// the real scoop binary and to allow argument verification.
    /// </param>
    public ScoopAdapter(Func<string, string[], Task<ProcessResult>> runAsync)
    {
        _runAsync = runAsync;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Uses a fast PATH probe — does not run a full scoop operation.
    /// </remarks>
    public bool IsAvailable()
    {
        return ProcessHelper.IsOnPath("scoop");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Runs <c>scoop list &lt;packageId&gt;</c>. Returns <see langword="true"/>
    /// when the exit code is 0; any non-zero exit code (including "not found") returns
    /// <see langword="false"/>.
    /// </remarks>
    public async Task<bool> IsInstalled(string packageId)
    {
        ProcessResult result = await _runAsync(
            "scoop",
            new[] { "list", packageId }).ConfigureAwait(false);

        return result.ExitCode == 0;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Runs <c>scoop list &lt;packageId&gt;</c> and parses the version from the
    /// tabular output. Returns <see langword="null"/> when the package is not installed
    /// (non-zero exit code) or the version cannot be parsed from the output.
    /// </remarks>
    public async Task<string?> GetInstalledVersion(string packageId)
    {
        ProcessResult result = await _runAsync(
            "scoop",
            new[] { "list", packageId }).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return null;
        }

        return ParseVersionFromListOutput(result.Stdout, packageId);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Runs <c>scoop install &lt;packageId&gt;</c>.
    /// </remarks>
    public Task<ProcessResult> Install(string packageId)
    {
        return _runAsync("scoop", new[] { "install", packageId });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Runs <c>scoop update &lt;packageId&gt;</c>.
    /// </remarks>
    public Task<ProcessResult> Update(string packageId)
    {
        return _runAsync("scoop", new[] { "update", packageId });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Runs <c>scoop uninstall &lt;packageId&gt;</c>.
    /// </remarks>
    public Task<ProcessResult> Uninstall(string packageId)
    {
        return _runAsync("scoop", new[] { "uninstall", packageId });
    }

    /// <summary>
    /// Ensures the <c>winix</c> scoop bucket is registered on this machine.
    /// If the bucket is already present in <c>scoop bucket list</c>, this method
    /// returns without making any further calls. Otherwise it runs
    /// <c>scoop bucket add winix https://github.com/Yortw/winix</c>.
    /// </summary>
    public async Task EnsureBucket()
    {
        ProcessResult listResult = await _runAsync(
            "scoop",
            new[] { "bucket", "list" }).ConfigureAwait(false);

        string[] lines = listResult.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (string line in lines)
        {
            if (line.Trim().Equals(BucketName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await _runAsync(
            "scoop",
            new[] { "bucket", "add", BucketName, BucketUrl }).ConfigureAwait(false);
    }

    /// <summary>
    /// Parses the installed version string from <c>scoop list</c> output.
    /// </summary>
    /// <param name="stdout">
    /// The stdout text from <c>scoop list &lt;packageId&gt;</c>.
    /// Expected format: a header block followed by rows of
    /// <c>Name Version Source</c> columns separated by whitespace.
    /// </param>
    /// <param name="packageId">
    /// The package name to locate in the output. Matched case-insensitively
    /// against the first token on each data row.
    /// </param>
    /// <returns>
    /// The version string (second whitespace-separated token on the matching row),
    /// or <see langword="null"/> when no matching row is found or the row has
    /// fewer than 2 tokens.
    /// </returns>
    internal static string? ParseVersionFromListOutput(string stdout, string packageId)
    {
        string[] lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (string line in lines)
        {
            string trimmed = line.Trim();

            if (!trimmed.StartsWith(packageId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 2)
            {
                return parts[1];
            }
        }

        return null;
    }
}
