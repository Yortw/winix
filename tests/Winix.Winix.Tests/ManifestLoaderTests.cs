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

    // Round-1 fresh-eyes 2026-05-09 SFH-I1 closure: corrupt cache no longer
    // locks the user out when a valid bundle exists — fall through with a
    // stderr warning naming the corrupt source.

    [Fact]
    public async Task LoadAsync_CorruptCacheValidBundle_FallsThroughToBundleWithWarning()
    {
        string cachePath = Path.Combine(Path.GetTempPath(), $"winix-cache-corrupt-{Guid.NewGuid():N}.json");
        string bundlePath = Path.Combine(Path.GetTempPath(), $"winix-bundle-{Guid.NewGuid():N}.json");
        try
        {
            // Cache exists and is fresher than bundle, but contains corrupt JSON.
            await File.WriteAllTextAsync(cachePath, "{ corrupt: not valid json @@");
            await File.WriteAllTextAsync(bundlePath, ValidJson);
            // Make the cache strictly newer.
            File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow);
            File.SetLastWriteTimeUtc(bundlePath, DateTime.UtcNow.AddMinutes(-1));

            using var warnings = new StringWriter();
            ToolManifest manifest = await ManifestLoader.LoadAsync(
                url: "http://localhost:1/never-reached",
                bundledPath: bundlePath,
                cachePath: cachePath,
                warnings: warnings);

            // Bundle won despite cache being newer — fallback succeeded.
            Assert.Equal("0.4.0", manifest.Version);
            // Warning was emitted naming the corrupt source.
            string warning = warnings.ToString();
            Assert.Contains("corrupt", warning, StringComparison.Ordinal);
            Assert.Contains(cachePath, warning, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(cachePath)) File.Delete(cachePath);
            if (File.Exists(bundlePath)) File.Delete(bundlePath);
        }
    }

    [Fact]
    public async Task LoadAsync_BothCorrupt_ThrowsOriginalFailure()
    {
        // Edge case: corrupt cache + corrupt bundle → rethrow the chosen-source
        // exception (still better than silent success).
        string cachePath = Path.Combine(Path.GetTempPath(), $"winix-cache-bothcorrupt-{Guid.NewGuid():N}.json");
        string bundlePath = Path.Combine(Path.GetTempPath(), $"winix-bundle-bothcorrupt-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(cachePath, "@@ invalid 1");
            await File.WriteAllTextAsync(bundlePath, "@@ invalid 2");

            await Assert.ThrowsAsync<ManifestParseException>(() => ManifestLoader.LoadAsync(
                bundledPath: bundlePath,
                cachePath: cachePath));
        }
        finally
        {
            if (File.Exists(cachePath)) File.Delete(cachePath);
            if (File.Exists(bundlePath)) File.Delete(bundlePath);
        }
    }

    // Round-1 fresh-eyes 2026-05-09 SFH-I2 closure: future-stamped cache mtime
    // no longer wins indefinitely — clamped to UtcNow with 5-min skew tolerance
    // so a fresher bundle wins normally.

    [Fact]
    public async Task LoadAsync_CacheMtimeFarInFuture_BundleWinsWhenBundleFresher()
    {
        string cachePath = Path.Combine(Path.GetTempPath(), $"winix-cache-future-{Guid.NewGuid():N}.json");
        string bundlePath = Path.Combine(Path.GetTempPath(), $"winix-bundle-future-{Guid.NewGuid():N}.json");
        try
        {
            // Cache is stamped 5 years in the future (incoherent — restored from a
            // backup that preserved future timestamps, OR clock skew on a roaming
            // laptop that briefly had the wrong year). Bundle is stamped now.
            string staleManifest = """
                {
                  "version": "0.0.1-stale",
                  "tools": {}
                }
                """;
            await File.WriteAllTextAsync(cachePath, staleManifest);
            await File.WriteAllTextAsync(bundlePath, ValidJson);

            File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow.AddYears(5));
            File.SetLastWriteTimeUtc(bundlePath, DateTime.UtcNow);

            ToolManifest manifest = await ManifestLoader.LoadAsync(
                url: "http://localhost:1/never-reached",
                bundledPath: bundlePath,
                cachePath: cachePath);

            // Bundle won despite cache mtime being later in raw clock time.
            Assert.Equal("0.4.0", manifest.Version);
            Assert.NotEqual("0.0.1-stale", manifest.Version);
        }
        finally
        {
            if (File.Exists(cachePath)) File.Delete(cachePath);
            if (File.Exists(bundlePath)) File.Delete(bundlePath);
        }
    }

    [Fact]
    public async Task LoadAsync_CacheMtimeWithinSkewTolerance_StillWins()
    {
        // Defensive: a cache stamped within the 5-minute skew tolerance is treated
        // as legitimate and wins normally. Pre-fix this was the only behaviour;
        // the SFH-I2 fix must not regress it.
        string cachePath = Path.Combine(Path.GetTempPath(), $"winix-cache-skew-{Guid.NewGuid():N}.json");
        string bundlePath = Path.Combine(Path.GetTempPath(), $"winix-bundle-skew-{Guid.NewGuid():N}.json");
        try
        {
            string newerCache = """
                {
                  "version": "0.5.0-fresh",
                  "tools": {}
                }
                """;
            await File.WriteAllTextAsync(cachePath, newerCache);
            await File.WriteAllTextAsync(bundlePath, ValidJson);

            // Cache stamped 1 minute in the future (well within skew tolerance).
            File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow.AddMinutes(1));
            File.SetLastWriteTimeUtc(bundlePath, DateTime.UtcNow.AddMinutes(-1));

            ToolManifest manifest = await ManifestLoader.LoadAsync(
                url: "http://localhost:1/never-reached",
                bundledPath: bundlePath,
                cachePath: cachePath);

            Assert.Equal("0.5.0-fresh", manifest.Version);
        }
        finally
        {
            if (File.Exists(cachePath)) File.Delete(cachePath);
            if (File.Exists(bundlePath)) File.Delete(bundlePath);
        }
    }
}
