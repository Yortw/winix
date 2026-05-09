#nullable enable

namespace Winix.Winix;

/// <summary>
/// Package manager adapter for <c>brew</c> (Homebrew, macOS/Linux).
/// Wraps brew CLI commands for install, upgrade, uninstall, version detection,
/// and automatic registration of the <c>yortw/winix</c> tap.
/// </summary>
public sealed class BrewAdapter : IPackageManagerAdapter
{
    private readonly Func<string, string[], Task<ProcessResult>> _runAsync;

    private const string TapName = "yortw/winix";

    /// <inheritdoc/>
    public string Name => "brew";

    /// <summary>
    /// Initialises a new <see cref="BrewAdapter"/> that calls the real brew process.
    /// </summary>
    public BrewAdapter() : this(ProcessHelper.RunAsync)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="BrewAdapter"/> with an injectable process runner.
    /// </summary>
    /// <param name="runAsync">
    /// Delegate used to invoke external processes. Injected in tests to avoid spawning
    /// the real brew binary and to allow argument verification.
    /// </param>
    public BrewAdapter(Func<string, string[], Task<ProcessResult>> runAsync)
    {
        _runAsync = runAsync;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Uses a fast PATH probe — does not run a full brew operation.
    /// </remarks>
    public bool IsAvailable()
    {
        return ProcessHelper.IsOnPath("brew");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Runs <c>brew list &lt;packageId&gt;</c>. Returns <see langword="true"/>
    /// when the exit code is 0; any non-zero exit code (including "not found") returns
    /// <see langword="false"/>.
    /// </remarks>
    public async Task<bool> IsInstalled(string packageId)
    {
        ProcessResult result = await _runAsync(
            "brew",
            new[] { "list", packageId }).ConfigureAwait(false);

        return result.ExitCode == 0;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Runs <c>brew list --versions &lt;packageId&gt;</c> and returns the last
    /// whitespace-separated token from the trimmed output (e.g. <c>"timeit 0.2.0"</c>
    /// yields <c>"0.2.0"</c>). If the output is already just the version string it
    /// is returned directly. Returns <see langword="null"/> when the package is not
    /// installed (non-zero exit code) or stdout is empty.
    /// </remarks>
    public async Task<string?> GetInstalledVersion(string packageId)
    {
        ProcessResult result = await _runAsync(
            "brew",
            new[] { "list", "--versions", packageId }).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return null;
        }

        string trimmed = result.Stdout.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        // Output is either "packagename version" or just "version" after trimming.
        string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts[parts.Length - 1];
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Runs <c>brew list --versions</c> (no filter) once. Output is one
    /// <c>name version1 [version2 …]</c> per installed formula, so we don't need
    /// fixed-width parsing — split each line on whitespace, take the first token as
    /// the name and the second as the version.
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, string?>> GetInstalled()
    {
        ProcessResult result = await _runAsync(
            "brew",
            new[] { "list", "--versions" }).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }

        return ParseListOutput(result.Stdout);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Runs <c>brew install yortw/winix/&lt;packageId&gt;</c> using the fully-qualified
    /// tap path so brew can locate the formula without requiring a prior tap.
    /// </remarks>
    public Task<ProcessResult> Install(string packageId)
    {
        return _runAsync("brew", new[] { "install", $"{TapName}/{packageId}" });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Runs <c>brew upgrade yortw/winix/&lt;packageId&gt;</c> using the fully-qualified
    /// tap path.
    /// </remarks>
    public Task<ProcessResult> Update(string packageId)
    {
        return _runAsync("brew", new[] { "upgrade", $"{TapName}/{packageId}" });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Runs <c>brew uninstall &lt;packageId&gt;</c> using just the bare package name;
    /// the tap prefix is not required for uninstall.
    /// </remarks>
    public Task<ProcessResult> Uninstall(string packageId)
    {
        return _runAsync("brew", new[] { "uninstall", packageId });
    }

    /// <summary>
    /// Ensures the <c>yortw/winix</c> Homebrew tap is registered on this machine.
    /// Runs <c>brew tap</c> to list current taps; if <c>yortw/winix</c> is not present,
    /// runs <c>brew tap yortw/winix</c> to add it and returns <see langword="true"/>.
    /// If the tap already exists this method returns <see langword="false"/> without
    /// making any further calls so callers can surface a one-time notice.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the tap was just registered by this call;
    /// <see langword="false"/> when it was already present.
    /// </returns>
    public async Task<bool> EnsureTap()
    {
        ProcessResult listResult = await _runAsync(
            "brew",
            new[] { "tap" }).ConfigureAwait(false);

        string[] lines = listResult.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (string line in lines)
        {
            if (line.Trim().Equals(TapName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        ProcessResult addResult = await _runAsync(
            "brew",
            new[] { "tap", TapName }).ConfigureAwait(false);

        // Round-1 fresh-eyes 2026-05-09 SFH-I3 + CR-I2 closure: same defect
        // class as ScoopAdapter.EnsureBucket — the result was discarded and
        // EnsureTap unconditionally returned true. The caller emitted
        // "registered brew tap 'yortw/winix'" on stderr even when the
        // underlying `brew tap` failed (network down, github unreachable).
        // Throw on non-zero so the caller's existing catch surfaces a
        // "could not add brew tap" warning instead.
        if (addResult.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"brew tap returned exit code {addResult.ExitCode}");
        }

        return true;
    }

    /// <summary>
    /// Parses the <c>brew list --versions</c> output into a dictionary keyed by
    /// formula name (case-insensitive). Each output line is
    /// <c>name version1 [version2 …]</c>; multiple installed versions on the same
    /// formula get the most-recent (last whitespace-separated) value, matching
    /// <see cref="GetInstalledVersion"/>'s "last token" convention.
    /// </summary>
    internal static IReadOnlyDictionary<string, string?> ParseListOutput(string stdout)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        string[] lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            string name = parts[0];
            string? version = parts.Length >= 2 ? parts[parts.Length - 1] : null;
            result[name] = version;
        }

        return result;
    }
}
