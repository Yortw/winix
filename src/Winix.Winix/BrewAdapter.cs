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
    /// runs <c>brew tap yortw/winix</c> to add it. If the tap already exists this
    /// method returns without making any further calls.
    /// </summary>
    public async Task EnsureTap()
    {
        ProcessResult listResult = await _runAsync(
            "brew",
            new[] { "tap" }).ConfigureAwait(false);

        string[] lines = listResult.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (string line in lines)
        {
            if (line.Trim().Equals(TapName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await _runAsync(
            "brew",
            new[] { "tap", TapName }).ConfigureAwait(false);
    }
}
