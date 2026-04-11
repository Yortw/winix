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
}
