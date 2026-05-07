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

    [Fact]
    public async Task LoadAsync_CacheNewerThanBundle_PrefersCache()
    {
        string bundlePath = Path.Combine(Path.GetTempPath(), $"winix-bundle-{Guid.NewGuid():N}.json");
        string cachePath = Path.Combine(Path.GetTempPath(), $"winix-cache-{Guid.NewGuid():N}.json");

        // Bundle has version 0.1, cache has version 0.4 — we expect 0.4 because cache is fresher.
        string bundleJson = ValidJson.Replace("0.4.0", "0.1.0");
        await File.WriteAllTextAsync(bundlePath, bundleJson);
        File.SetLastWriteTimeUtc(bundlePath, DateTime.UtcNow.AddDays(-30));

        await File.WriteAllTextAsync(cachePath, ValidJson);
        File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow);

        try
        {
            ToolManifest manifest = await ManifestLoader.LoadAsync(
                bundledPath: bundlePath,
                cachePath: cachePath);

            Assert.Equal("0.4.0", manifest.Version);
        }
        finally
        {
            File.Delete(bundlePath);
            File.Delete(cachePath);
        }
    }

    [Fact]
    public async Task LoadAsync_BundleNewerThanCache_PrefersBundle()
    {
        string bundlePath = Path.Combine(Path.GetTempPath(), $"winix-bundle-{Guid.NewGuid():N}.json");
        string cachePath = Path.Combine(Path.GetTempPath(), $"winix-cache-{Guid.NewGuid():N}.json");

        // Cache has stale version 0.1, bundle has version 0.4 (a recent release shipped).
        string staleCacheJson = ValidJson.Replace("0.4.0", "0.1.0");
        await File.WriteAllTextAsync(cachePath, staleCacheJson);
        File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow.AddDays(-30));

        await File.WriteAllTextAsync(bundlePath, ValidJson);
        File.SetLastWriteTimeUtc(bundlePath, DateTime.UtcNow);

        try
        {
            ToolManifest manifest = await ManifestLoader.LoadAsync(
                bundledPath: bundlePath,
                cachePath: cachePath);

            Assert.Equal("0.4.0", manifest.Version);
        }
        finally
        {
            File.Delete(bundlePath);
            File.Delete(cachePath);
        }
    }

    [Fact]
    public async Task RefreshFromNetworkAsync_NetworkUnreachable_DoesNotOverwriteCache()
    {
        string cachePath = Path.Combine(Path.GetTempPath(), $"winix-cache-{Guid.NewGuid():N}.json");
        string original = ValidJson.Replace("0.4.0", "0.3.0");
        await File.WriteAllTextAsync(cachePath, original);

        try
        {
            // The network call fails — RefreshFromNetworkAsync must surface the failure
            // and leave the existing cache file unchanged.
            await Assert.ThrowsAsync<ManifestParseException>(
                () => ManifestLoader.RefreshFromNetworkAsync(
                    url: "http://localhost:1/unreachable",
                    cachePath: cachePath));

            string after = await File.ReadAllTextAsync(cachePath);
            Assert.Equal(original, after);
        }
        finally
        {
            File.Delete(cachePath);
        }
    }
}
