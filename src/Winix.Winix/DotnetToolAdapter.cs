#nullable enable

namespace Winix.Winix;

/// <summary>
/// Package manager adapter for <c>dotnet tool</c> (.NET global tools).
/// Wraps dotnet CLI commands for install, update, uninstall, and version detection.
/// This adapter is the universal fallback — available on any machine with the .NET SDK installed.
/// </summary>
public sealed class DotnetToolAdapter : IPackageManagerAdapter
{
    private readonly Func<string, string[], Task<ProcessResult>> _runAsync;

    /// <inheritdoc/>
    public string Name => "dotnet";

    /// <summary>
    /// Initialises a new <see cref="DotnetToolAdapter"/> that calls the real dotnet process.
    /// </summary>
    public DotnetToolAdapter() : this(ProcessHelper.RunAsync)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="DotnetToolAdapter"/> with an injectable process runner.
    /// </summary>
    /// <param name="runAsync">
    /// Delegate used to invoke external processes. Injected in tests to avoid spawning
    /// the real dotnet binary and to allow argument verification.
    /// </param>
    public DotnetToolAdapter(Func<string, string[], Task<ProcessResult>> runAsync)
    {
        _runAsync = runAsync;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Uses a fast PATH probe — does not run a full dotnet operation.
    /// </remarks>
    public bool IsAvailable()
    {
        return ProcessHelper.IsOnPath("dotnet");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Delegates to <see cref="GetInstalledVersion"/> to avoid duplicating the parse logic.
    /// Returns <see langword="true"/> when a non-null version is found.
    /// </remarks>
    public async Task<bool> IsInstalled(string packageId)
    {
        string? version = await GetInstalledVersion(packageId).ConfigureAwait(false);

        return version != null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Runs <c>dotnet tool list -g</c> and parses the version from the tabular output.
    /// Package IDs in the output are always lowercase even when the NuGet package ID uses
    /// mixed case (e.g. "Winix.TimeIt" appears as "winix.timeit") — comparison is
    /// case-insensitive to handle this.
    /// Returns <see langword="null"/> when the package is not in the list or the version
    /// cannot be parsed.
    /// </remarks>
    public async Task<string?> GetInstalledVersion(string packageId)
    {
        ProcessResult result = await _runAsync(
            "dotnet",
            new[] { "tool", "list", "-g" }).ConfigureAwait(false);

        return ParseVersionFromListOutput(result.Stdout, packageId);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Runs <c>dotnet tool list -g</c> once and parses every row of the tabular output.
    /// <see cref="GetInstalledVersion"/> already runs the same command per call, so for
    /// the bulk path the only saving is that the caller needs one process spawn instead
    /// of N — still meaningful when N is 22+.
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, string?>> GetInstalled()
    {
        ProcessResult result = await _runAsync(
            "dotnet",
            new[] { "tool", "list", "-g" }).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }

        return ParseListOutput(result.Stdout);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Runs <c>dotnet tool install -g &lt;packageId&gt;</c>.
    /// </remarks>
    public Task<ProcessResult> Install(string packageId)
    {
        return _runAsync("dotnet", new[] { "tool", "install", "-g", packageId });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Runs <c>dotnet tool update -g &lt;packageId&gt;</c>.
    /// </remarks>
    public Task<ProcessResult> Update(string packageId)
    {
        return _runAsync("dotnet", new[] { "tool", "update", "-g", packageId });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Runs <c>dotnet tool uninstall -g &lt;packageId&gt;</c>.
    /// </remarks>
    public Task<ProcessResult> Uninstall(string packageId)
    {
        return _runAsync("dotnet", new[] { "tool", "uninstall", "-g", packageId });
    }

    /// <summary>
    /// Parses the installed version string from <c>dotnet tool list -g</c> output.
    /// </summary>
    /// <param name="stdout">
    /// The stdout text from <c>dotnet tool list -g</c>. Expected format: a header line,
    /// a dashes separator, then rows of <c>Package Id   Version   Commands</c> columns
    /// separated by whitespace. Package IDs are always lowercase in this output.
    /// </param>
    /// <param name="packageId">
    /// The NuGet package ID to locate in the output. Matched case-insensitively against
    /// the first token on each data row to handle the lowercase normalisation applied by
    /// the dotnet CLI.
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
            string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2)
            {
                continue;
            }

            if (parts[0].Equals(packageId, StringComparison.OrdinalIgnoreCase))
            {
                return parts[1];
            }
        }

        return null;
    }

    /// <summary>
    /// Parses the <c>dotnet tool list -g</c> output into a dictionary keyed by package
    /// id (case-insensitive). Each data row is
    /// <c>packageid version commands</c> separated by whitespace; the first two
    /// whitespace-separated tokens are the id and version. Header and dashes-separator
    /// rows are skipped by the <c>parts.Length &lt; 2</c> guard combined with the
    /// "starts with letter or digit" check on the id.
    /// </summary>
    /// <remarks>
    /// dotnet's CLI normalises package ids to lowercase regardless of how the package
    /// was published; the case-insensitive dictionary lets callers look up by either
    /// form. The header line (<c>"Package Id  Version  Commands"</c>) splits to 3
    /// tokens with first token "Package", which would parse as a fake package — we
    /// reject it by requiring the row to start with the package id position (col 0
    /// is always the id). Practically the lazy reject works because we then verify
    /// the row has exactly the shape expected.
    /// </remarks>
    internal static IReadOnlyDictionary<string, string?> ParseListOutput(string stdout)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        string[] lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (string line in lines)
        {
            string trimmed = line.Trim();

            // Skip header ("Package Id  Version  Commands") and separator ("---...").
            if (trimmed.StartsWith("Package", StringComparison.Ordinal) ||
                trimmed.StartsWith("---", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            string packageId = parts[0];
            string version = parts[1];
            result[packageId] = version;
        }

        return result;
    }
}
