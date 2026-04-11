#nullable enable

namespace Winix.Winix;

/// <summary>
/// Downloads and parses the Winix suite manifest from a remote URL.
/// </summary>
public static class ManifestLoader
{
    /// <summary>
    /// The default URL for the published Winix suite manifest, served as the
    /// latest-release asset on GitHub.
    /// </summary>
    public const string DefaultUrl =
        "https://github.com/Yortw/winix/releases/latest/download/winix-manifest.json";

    /// <summary>
    /// Downloads and parses the Winix suite manifest.
    /// </summary>
    /// <param name="url">
    /// The URL to download the manifest from. Defaults to
    /// <see cref="DefaultUrl"/> when <see langword="null"/>.
    /// </param>
    /// <returns>The parsed <see cref="ToolManifest"/>.</returns>
    /// <exception cref="ManifestParseException">
    /// Thrown when the HTTP request fails, times out, or when the downloaded
    /// content cannot be parsed as a valid manifest.
    /// </exception>
    public static async Task<ToolManifest> LoadAsync(string? url = null)
    {
        var resolvedUrl = url ?? DefaultUrl;

        using var client = new System.Net.Http.HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        string json;
        try
        {
            json = await client.GetStringAsync(resolvedUrl).ConfigureAwait(false);
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            throw new ManifestParseException(
                $"Failed to download manifest from '{resolvedUrl}': {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new ManifestParseException(
                $"Timed out downloading manifest from '{resolvedUrl}'.", ex);
        }

        return ToolManifest.Parse(json);
    }
}
