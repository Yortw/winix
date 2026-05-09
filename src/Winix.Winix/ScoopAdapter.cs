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
    /// Runs <c>scoop list</c> (no filter) once and parses every row of the tabular
    /// output. See <see cref="WingetAdapter.GetInstalled"/> for the rationale —
    /// <c>scoop list</c>'s per-package filter still walks the full app directory, so
    /// the unfiltered call is at most O(N) faster when checking N tools.
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, string?>> GetInstalled()
    {
        ProcessResult result = await _runAsync(
            "scoop",
            new[] { "list" }).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }

        return ParseListOutput(result.Stdout);
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
    /// returns <see langword="false"/> without making any further calls. Otherwise it
    /// runs <c>scoop bucket add winix https://github.com/Yortw/winix</c> and returns
    /// <see langword="true"/> so callers can surface a "now registered" notice on
    /// the first call only and stay quiet on subsequent ones.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the bucket was just registered by this call;
    /// <see langword="false"/> when it was already present.
    /// </returns>
    public async Task<bool> EnsureBucket()
    {
        ProcessResult listResult = await _runAsync(
            "scoop",
            new[] { "bucket", "list" }).ConfigureAwait(false);

        string[] lines = listResult.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (string line in lines)
        {
            if (line.Trim().Equals(BucketName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        ProcessResult addResult = await _runAsync(
            "scoop",
            new[] { "bucket", "add", BucketName, BucketUrl }).ConfigureAwait(false);

        // Round-1 fresh-eyes 2026-05-09 SFH-I3 + CR-I2 closure: pre-fix the
        // result was discarded and EnsureBucket unconditionally returned true.
        // The caller emitted "registered scoop bucket 'winix'" on stderr even
        // when the underlying `scoop bucket add` failed (network down, git
        // missing from PATH, repo unreachable from this machine). The user
        // got a misleading positive notice, then the subsequent `scoop install
        // <tool>` produced confusing "couldn't find manifest" errors with no
        // pointer to the real cause. Throw on non-zero so the caller's
        // existing catch surfaces a "could not register" warning instead.
        if (addResult.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"scoop bucket add returned exit code {addResult.ExitCode}");
        }

        return true;
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

    /// <summary>
    /// Parses the full <c>scoop list</c> tabular output into a dictionary keyed by app
    /// name (case-insensitive). The Version cell is the value, or <see langword="null"/>
    /// when the row's Version column is empty (scoop emits empty Version for failed
    /// installs — observed on local dev machine where <c>timeit</c> was registered but
    /// the install errored partway).
    /// </summary>
    /// <remarks>
    /// scoop's tabular output starts with <c>Installed apps:</c>, an empty line, then
    /// the header <c>Name | Version | Source | Updated | Info</c>, a dashes-separator
    /// line, and finally the data rows. Columns are fixed-width as with winget, so the
    /// parsing strategy is the same: locate the header, identify column start
    /// positions, slice each data row at those offsets.
    /// </remarks>
    internal static IReadOnlyDictionary<string, string?> ParseListOutput(string stdout)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        string[] lines = stdout.Split('\n');

        int headerIdx = -1;
        for (int i = 0; i < lines.Length - 1; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith("Name", StringComparison.Ordinal))
            {
                continue;
            }

            string nextTrimmed = lines[i + 1].TrimStart();
            if (nextTrimmed.StartsWith("---", StringComparison.Ordinal))
            {
                headerIdx = i;
                break;
            }
        }

        if (headerIdx < 0)
        {
            return result;
        }

        string header = lines[headerIdx];
        int nameCol = header.IndexOf("Name", StringComparison.Ordinal);
        int versionCol = header.IndexOf("Version", StringComparison.Ordinal);
        int sourceCol = header.IndexOf("Source", StringComparison.Ordinal);

        if (nameCol < 0 || versionCol <= nameCol)
        {
            return result;
        }

        // Version ends at the start of "Source" if present, else end-of-line.
        int versionEnd = sourceCol > versionCol ? sourceCol : -1;

        for (int i = headerIdx + 2; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.EndsWith("\r", StringComparison.Ordinal))
            {
                line = line[..^1];
            }

            if (line.Length <= nameCol)
            {
                continue;
            }

            int nameEnd = Math.Min(versionCol, line.Length);
            string name = line.Substring(nameCol, nameEnd - nameCol).Trim();

            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            string? version = null;
            if (line.Length > versionCol)
            {
                int verEnd = versionEnd > 0 ? Math.Min(versionEnd, line.Length) : line.Length;
                string raw = line.Substring(versionCol, verEnd - versionCol).Trim();
                if (!string.IsNullOrEmpty(raw))
                {
                    version = raw;
                }
            }

            result[name] = version;
        }

        return result;
    }
}
