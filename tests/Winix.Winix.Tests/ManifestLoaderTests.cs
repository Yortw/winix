#nullable enable

using System.IO;
using System.Threading.Tasks;
using Xunit;
using Winix.Winix;

namespace Winix.Winix.Tests;

public class ManifestLoaderTests
{
    private const string ValidJson = """
        {
          "version": "0.4.0",
          "tools": {
            "timeit": {
              "description": "Time a command.",
              "packages": { "winget": "Winix.TimeIt" }
            }
          }
        }
        """;

    [Fact]
    public async Task LoadAsync_BundledFilePresent_LoadsFromBundleWithoutNetwork()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"winix-bundle-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(tempPath, ValidJson);
        try
        {
            // Pass a clearly invalid URL — the test fails (and surfaces a network call) if
            // the loader doesn't honour the bundle-first contract.
            ToolManifest manifest = await ManifestLoader.LoadAsync(
                url: "http://localhost:1/never-reached",
                bundledPath: tempPath);

            Assert.Equal("0.4.0", manifest.Version);
            Assert.True(manifest.Tools.ContainsKey("timeit"));
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task LoadAsync_BundledFileMissing_FallsBackToUrl()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"winix-bundle-missing-{Guid.NewGuid():N}.json");
        Assert.False(File.Exists(missing));

        // The url is unreachable — we expect a ManifestParseException to surface, proving
        // the fallback path was taken (rather than throwing on the missing bundle directly).
        ManifestParseException ex = await Assert.ThrowsAsync<ManifestParseException>(
            () => ManifestLoader.LoadAsync(
                url: "http://localhost:1/never-resolves",
                bundledPath: missing));

        Assert.Contains("download", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_BundledFileInvalidJson_ThrowsWithoutNetworkFallback()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"winix-bundle-bad-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(tempPath, "{ this is not valid json");
        try
        {
            // A corrupt bundle is an internal-error condition — we never silently fall back
            // to the network because that would mask a real packaging bug.
            await Assert.ThrowsAsync<ManifestParseException>(
                () => ManifestLoader.LoadAsync(
                    url: "http://localhost:1/never-reached",
                    bundledPath: tempPath));
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
