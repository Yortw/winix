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
    /// Runs <c>winget list</c> (no filter) once and parses every row of the tabular
    /// output into a dictionary keyed by Id. Suite-wide flows (<c>winix list</c>,
    /// <c>winix status</c>, <c>winix uninstall</c>) measured at ~7 minutes when issuing
    /// 22 filtered <see cref="IsInstalled"/> calls; the unfiltered call returns the same
    /// information in ~8 seconds because winget only walks its index once.
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, string?>> GetInstalled()
    {
        ProcessResult result = await _runAsync(
            "winget",
            new[] { "list" }).ConfigureAwait(false);

        // winget list returns 0 even when the table is empty; non-zero indicates a real
        // failure (winget missing, network down for the index refresh, etc.). Surface
        // an empty snapshot rather than throwing — the caller will treat every tool as
        // not-installed, which is the same outcome the per-package path produces on
        // adapter failure (IsInstalled returns false on non-zero exit).
        if (result.ExitCode != 0)
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }

        return ParseListOutput(result.Stdout);
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
    /// Expected format: a header line, a dashes separator, then rows whose
    /// columns are <c>Name Id Version [Available [Source]]</c> separated by
    /// whitespace. The Available and Source columns are present whenever an
    /// upgrade is pending — i.e. on any installed-but-out-of-date package.
    /// </param>
    /// <param name="packageId">
    /// The package ID to locate in the output. Matched case-insensitively.
    /// </param>
    /// <returns>
    /// The installed version string (the token immediately after the Id column),
    /// or <see langword="null"/> when no matching row is found.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Round-1 fresh-eyes 2026-05-09 SFH-C1 closure: pre-fix this method returned
    /// <c>parts[parts.Length - 1]</c> — the LAST whitespace-separated token on
    /// the matching row. That was correct only for the 3-column shape (<c>Name
    /// Id Version</c>); whenever <c>winget list</c> emitted the 5-column shape
    /// for an installed-but-upgradable package (<c>Name Id Version Available
    /// Source</c>) the last token was the source name (<c>"winix"</c>) or the
    /// available version, not the installed version. The bug shipped silently
    /// through both <c>winix list</c>'s human-readable table AND the
    /// <c>winix list --json</c> output's <c>version</c> field.
    /// </para>
    /// <para>
    /// The fix anchors on the Id column instead of the row-end: scanning the
    /// row right-to-left for a token that case-insensitively equals the package
    /// id, and returning the next token. This is robust against multi-word
    /// Names (e.g. <c>"Visual Studio"</c>), the optional Available/Source
    /// columns, and any future column additions winget makes after Source.
    /// </para>
    /// </remarks>
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

            // Find the LAST token that case-insensitively equals packageId. This
            // anchors on the Id column — which is at index 1 by default but
            // shifts right when Name has embedded spaces. Scanning right-to-left
            // finds Id even when Name happens to equal Id (the common case for
            // Winix's own tools where Name and Id are both "Winix.TimeIt"); in
            // that case we want the second occurrence (the Id column), not the
            // first (the Name column).
            int idColIndex = -1;
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                if (string.Equals(parts[i], packageId, StringComparison.OrdinalIgnoreCase))
                {
                    idColIndex = i;
                    break;
                }
            }

            if (idColIndex < 0 || idColIndex + 1 >= parts.Length)
            {
                return null;
            }

            return parts[idColIndex + 1];
        }

        return null;
    }

    /// <summary>
    /// Parses the full <c>winget list</c> tabular output into a dictionary keyed by
    /// the package Id (case-insensitive). The Version cell is the value, or
    /// <see langword="null"/> when the row's Version column is empty (winget can emit
    /// rows with no version for ARP-detected packages — see the row format described
    /// in the remarks).
    /// </summary>
    /// <remarks>
    /// <para>
    /// winget's tabular output uses fixed-width columns. The header line is
    /// <c>Name … Id … Version … [Available … Source]</c>. The 5-column shape appears
    /// whenever any row has an upgrade pending or a Source attribution. Names are
    /// truncated with <c>…</c> to fit their column when too long.
    /// </para>
    /// <para>
    /// Because Names can contain whitespace and may be truncated mid-word, we cannot
    /// rely on whitespace-split column counts. Instead we parse the header to find
    /// each column's start position and slice each data row at those byte offsets.
    /// </para>
    /// <para>
    /// winget prefixes its stdout with a progress spinner (e.g. <c>"   -    \    |    /"</c>)
    /// before the actual table — sometimes hundreds of characters of garbage. The
    /// header detection (a line whose trimmed prefix is <c>"Name"</c> followed by a
    /// dashes-separator line) skips this prefix naturally.
    /// </para>
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
        int idCol = header.IndexOf("Id", StringComparison.Ordinal);
        int versionCol = header.IndexOf("Version", StringComparison.Ordinal);
        int availableCol = header.IndexOf("Available", StringComparison.Ordinal);
        int sourceCol = header.IndexOf("Source", StringComparison.Ordinal);

        if (idCol < 0 || versionCol <= idCol)
        {
            return result;
        }

        // Version column ends at whichever column comes next, or end-of-line.
        int versionEnd;
        if (availableCol > versionCol)
        {
            versionEnd = availableCol;
        }
        else if (sourceCol > versionCol)
        {
            versionEnd = sourceCol;
        }
        else
        {
            versionEnd = -1;
        }

        for (int i = headerIdx + 2; i < lines.Length; i++)
        {
            string line = lines[i];
            // winget often appends a CR on each line on Windows; strip it before slicing
            // so the trailing column doesn't pick up a stray '\r'.
            if (line.EndsWith("\r", StringComparison.Ordinal))
            {
                line = line[..^1];
            }

            if (line.Length <= idCol)
            {
                continue;
            }

            int idEnd = Math.Min(versionCol, line.Length);
            string id = line.Substring(idCol, idEnd - idCol).Trim();

            if (string.IsNullOrEmpty(id))
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

            // Last write wins — winget rarely emits duplicate Ids but if it does, the
            // later row almost always reflects the more recent state (e.g. a re-install
            // mid-run). Using the indexer rather than Add avoids a throw.
            result[id] = version;
        }

        return result;
    }
}
