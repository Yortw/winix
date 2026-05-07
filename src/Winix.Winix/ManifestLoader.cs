#nullable enable

namespace Winix.Winix;

/// <summary>
/// Resolves and parses the Winix suite manifest. Prefers a manifest bundled next to the
/// running binary; falls back to a network fetch when no bundle is present (typical of
/// dev <c>dotnet run</c> builds where the publish-output layout is absent).
/// </summary>
/// <remarks>
/// The bundled-first strategy keeps <c>winix list</c> instant and offline-safe, and ensures
/// the user always sees the canonical tool set that shipped with their installed binary —
/// preventing the "stale GitHub release manifest" failure mode where the published asset
/// lists fewer tools than the binary was built for. Network fetch is preserved as the
/// fallback so dev workflows (where the bundle is absent) keep working.
/// </remarks>
public static class ManifestLoader
{
    /// <summary>
    /// The default URL for the published Winix suite manifest, served as the
    /// latest-release asset on GitHub.
    /// </summary>
    public const string DefaultUrl =
        "https://github.com/Yortw/winix/releases/latest/download/winix-manifest.json";

    /// <summary>
    /// The publish-relative path of the bundled manifest, mirroring the man tool's
    /// bundled-pages convention (<c>share/man/man1/...</c>).
    /// </summary>
    internal const string BundledRelativePath = "share/winix/winix-manifest.json";

    /// <summary>
    /// Loads and parses the Winix suite manifest. Tries the bundled file first; falls back
    /// to a network fetch when no bundled manifest is found beside the binary.
    /// </summary>
    /// <param name="url">
    /// The URL to download the manifest from when no bundled copy is available. Defaults
    /// to <see cref="DefaultUrl"/> when <see langword="null"/>. Tests can pass an override.
    /// </param>
    /// <param name="bundledPath">
    /// Override for the bundled manifest path. Tests pass this to inject a fixture; production
    /// code leaves it <see langword="null"/> so the path is computed relative to the binary.
    /// </param>
    /// <returns>The parsed <see cref="ToolManifest"/>.</returns>
    /// <exception cref="ManifestParseException">
    /// Thrown when the bundled manifest is invalid, or — if no bundled manifest is present —
    /// when the network request fails, times out, or returns content that cannot be parsed.
    /// </exception>
    public static async Task<ToolManifest> LoadAsync(string? url = null, string? bundledPath = null)
    {
        string resolvedBundlePath = bundledPath ?? DefaultBundledPath();
        if (System.IO.File.Exists(resolvedBundlePath))
        {
            // The bundled copy is canonical when present. We surface parse failures
            // immediately rather than masking them by falling through to the network —
            // a corrupt bundle is an internal-error condition the operator must see.
            string bundledJson;
            try
            {
                bundledJson = await System.IO.File.ReadAllTextAsync(resolvedBundlePath).ConfigureAwait(false);
            }
            catch (System.IO.IOException ex)
            {
                throw new ManifestParseException(
                    $"Failed to read bundled manifest at '{resolvedBundlePath}' ({ex.GetType().Name}).", ex);
            }

            return ToolManifest.Parse(bundledJson);
        }

        return await LoadFromUrlAsync(url ?? DefaultUrl).ConfigureAwait(false);
    }

    /// <summary>
    /// Computes the absolute path to the bundled manifest based on the running binary's
    /// directory. Layout matches the man tool's bundled-pages convention so a single
    /// <c>share/</c> tree under the binary's directory hosts all bundled assets.
    /// </summary>
    private static string DefaultBundledPath()
    {
        return System.IO.Path.Combine(AppContext.BaseDirectory, "share", "winix", "winix-manifest.json");
    }

    /// <summary>
    /// Downloads the manifest from the given URL and parses it. Used as the fallback path
    /// when no bundled manifest is present, and exposed publicly so tests and future
    /// refresh logic can drive the network path directly.
    /// </summary>
    /// <param name="url">The URL to fetch the manifest from.</param>
    /// <exception cref="ManifestParseException">
    /// Thrown when the request fails, times out, or returns content that cannot be parsed.
    /// </exception>
    public static async Task<ToolManifest> LoadFromUrlAsync(string url)
    {
        using var client = new System.Net.Http.HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        string json;
        try
        {
            json = await client.GetStringAsync(url).ConfigureAwait(false);
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            throw new ManifestParseException(
                $"Failed to download manifest from '{url}': {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new ManifestParseException(
                $"Timed out downloading manifest from '{url}'.", ex);
        }

        return ToolManifest.Parse(json);
    }
}
