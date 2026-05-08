#nullable enable

namespace Winix.Winix;

/// <summary>
/// Resolves and parses the Winix suite manifest. Reads from a per-user cache file or a
/// manifest bundled next to the running binary — whichever is freshest by file mtime —
/// and falls back to a network fetch only when neither local source is available
/// (typical of dev <c>dotnet run</c> builds where the publish-output layout is absent).
/// </summary>
/// <remarks>
/// <para>
/// The local-first strategy keeps <c>winix list</c>, <c>status</c>, and <c>uninstall</c>
/// instant and offline-safe. The bundle is the floor: every released binary ships with
/// a current manifest so a user with no network and no cache still sees the canonical
/// tool set. The cache layers on top: when an explicit refresh has succeeded since the
/// release, the cache holds a newer view than the bundle, and that view is preferred.
/// </para>
/// <para>
/// "Whichever is newer by mtime" obviates a TTL: a stale cache from years ago will lose
/// to a fresh bundle from a recent release, and a recent refresh will beat the bundle of
/// a release the user is still on. Network refreshes only happen when an explicit
/// <see cref="RefreshFromNetworkAsync"/> caller drives them — never as a side effect of
/// <see cref="LoadAsync"/>, which is read-only and quick.
/// </para>
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
    /// The manifest filename used in the per-user cache directory.
    /// </summary>
    internal const string CacheFileName = "winix-manifest.json";

    /// <summary>
    /// Loads and parses the Winix suite manifest. Picks the freshest available source —
    /// the cache and the bundle are compared by file mtime, and whichever is newer wins.
    /// Network fetch is reserved for environments with no local source (dev builds).
    /// </summary>
    /// <param name="url">
    /// The URL to download the manifest from when no local source is available. Defaults
    /// to <see cref="DefaultUrl"/> when <see langword="null"/>.
    /// </param>
    /// <param name="bundledPath">
    /// Override for the bundled manifest path. Tests pass this to inject a fixture;
    /// production code leaves it <see langword="null"/>.
    /// </param>
    /// <param name="cachePath">
    /// Override for the cache file path. Tests pass this to inject a fixture; production
    /// code leaves it <see langword="null"/>.
    /// </param>
    /// <returns>The parsed <see cref="ToolManifest"/>.</returns>
    /// <exception cref="ManifestParseException">
    /// Thrown when the chosen local source is invalid (cache or bundle), or — when no
    /// local source is present — when the network request fails or returns invalid content.
    /// </exception>
    public static async Task<ToolManifest> LoadAsync(
        string? url = null,
        string? bundledPath = null,
        string? cachePath = null,
        TextWriter? warnings = null)
    {
        string resolvedBundlePath = bundledPath ?? DefaultBundledPath();
        string resolvedCachePath = cachePath ?? DefaultCachePath();

        string? chosenPath = SelectFreshestLocalSource(resolvedCachePath, resolvedBundlePath);
        if (chosenPath != null)
        {
            // Try the chosen path first. If it parses, return it.
            //
            // Round-1 fresh-eyes 2026-05-09 SFH-I1 closure: pre-fix a parse
            // failure on the chosen path threw immediately with no fallback,
            // which meant a single cache-write tear (interrupted I/O, AV
            // quarantine repair, partial sync) locked the user out of every
            // `winix list/install/uninstall` call until they manually deleted
            // the cache file — a recovery procedure they had no documentation
            // for. Now: when the chosen path fails to parse AND the other
            // local source exists and parses, fall through with a stderr
            // warning naming the corrupt source. The corrupt cache will be
            // overwritten on the next `RefreshFromNetworkAsync` call.
            try
            {
                return await ReadAndParseAsync(chosenPath).ConfigureAwait(false);
            }
            catch (ManifestParseException primaryFailure)
            {
                // Try the other local source as a fallback if it exists.
                string? fallbackPath = chosenPath == resolvedCachePath ? resolvedBundlePath : resolvedCachePath;
                if (!string.IsNullOrEmpty(fallbackPath) && System.IO.File.Exists(fallbackPath))
                {
                    try
                    {
                        ToolManifest fallback = await ReadAndParseAsync(fallbackPath).ConfigureAwait(false);
                        warnings?.WriteLine($"winix: warning: manifest at '{chosenPath}' is corrupt; falling back to '{fallbackPath}' ({primaryFailure.GetType().Name})");
                        return fallback;
                    }
                    catch (ManifestParseException)
                    {
                        // Both local sources are corrupt — surface the original failure
                        // (the chosen one — newer mtime, more likely to be the intended view).
                    }
                }

                throw;
            }
        }

        return await LoadFromUrlAsync(url ?? DefaultUrl).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a manifest file and parses it, wrapping read failures in
    /// <see cref="ManifestParseException"/> so the caller's catch sees a single class.
    /// </summary>
    private static async Task<ToolManifest> ReadAndParseAsync(string path)
    {
        string json;
        try
        {
            json = await System.IO.File.ReadAllTextAsync(path).ConfigureAwait(false);
        }
        catch (System.IO.IOException ex)
        {
            throw new ManifestParseException(
                $"Failed to read manifest at '{path}' ({ex.GetType().Name}).", ex);
        }

        return ToolManifest.Parse(json);
    }

    /// <summary>
    /// Fetches the manifest from the network and writes it to the per-user cache so future
    /// <see cref="LoadAsync"/> calls see the fresher view. Callers should treat this as a
    /// best-effort refresh: any network failure surfaces as <see cref="ManifestParseException"/>
    /// and the existing cache (if any) is left untouched.
    /// </summary>
    /// <param name="url">
    /// The URL to fetch the manifest from. Defaults to <see cref="DefaultUrl"/> when
    /// <see langword="null"/>.
    /// </param>
    /// <param name="cachePath">
    /// Override for the cache file path. Production code passes <see langword="null"/>;
    /// tests pass a temp path.
    /// </param>
    /// <returns>The parsed <see cref="ToolManifest"/> as fetched from the network.</returns>
    /// <exception cref="ManifestParseException">
    /// Thrown when the request fails, times out, returns invalid JSON, or when the cache
    /// write itself fails (the latter being unusual but worth surfacing — a refresh that
    /// can't persist defeats the whole point of caching).
    /// </exception>
    public static async Task<ToolManifest> RefreshFromNetworkAsync(
        string? url = null,
        string? cachePath = null)
    {
        string resolvedUrl = url ?? DefaultUrl;
        string resolvedCachePath = cachePath ?? DefaultCachePath();

        // Fetch raw bytes once; parse to validate; then persist. We never overwrite the
        // cache before parse succeeds — server-returned junk (HTML error page, truncated
        // body) shouldn't replace a good cached copy.
        string raw = await FetchRawAsync(resolvedUrl).ConfigureAwait(false);
        ToolManifest manifest = ToolManifest.Parse(raw);

        if (string.IsNullOrEmpty(resolvedCachePath))
        {
            // No usable cache root on this machine. The refresh still parses and returns
            // the fresh manifest; persistence is silently skipped.
            return manifest;
        }

        try
        {
            string? cacheDir = System.IO.Path.GetDirectoryName(resolvedCachePath);
            if (!string.IsNullOrEmpty(cacheDir))
            {
                System.IO.Directory.CreateDirectory(cacheDir);
            }
            await System.IO.File.WriteAllTextAsync(resolvedCachePath, raw).ConfigureAwait(false);
        }
        catch (System.IO.IOException ex)
        {
            throw new ManifestParseException(
                $"Failed to write manifest cache at '{resolvedCachePath}' ({ex.GetType().Name}).", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ManifestParseException(
                $"Permission denied writing manifest cache at '{resolvedCachePath}'.", ex);
        }

        return manifest;
    }

    /// <summary>
    /// Returns the absolute cache path for the current user, or <see langword="null"/>
    /// when no suitable cache root can be derived (e.g. minimal sandbox without
    /// <c>LOCALAPPDATA</c> or a HOME variable). Public so tests and tooling can introspect
    /// the location without having to mirror the resolution logic.
    /// </summary>
    public static string? GetDefaultCachePath()
    {
        return DefaultCachePath();
    }

    /// <summary>
    /// Compares the cache and bundle by mtime and returns whichever exists with the
    /// later mtime, or the only one that exists, or <see langword="null"/> when neither
    /// is present. Treats a missing path as "infinitely old" so a present file always
    /// wins against an absent one.
    /// </summary>
    /// <remarks>
    /// Round-1 fresh-eyes 2026-05-09 SFH-I2 closure: a cache file with a future mtime
    /// (clock skew on a roaming laptop, restored backup that preserved future
    /// timestamps, or any user-touched mtime) used to win against a fresher bundle
    /// indefinitely — even when the bundle shipped months later in a real release.
    /// User would install the current Winix release but `winix list` operated against
    /// last release's tool list; newly-added tools absent, removed tools still
    /// present, diagnostically opaque. Now: a cache mtime more than 5 minutes in the
    /// future of <c>UtcNow</c> is clamped to <c>UtcNow</c>, so a fresher bundle wins
    /// normally. The 5-minute tolerance absorbs ordinary NTP jitter without
    /// punishing legitimate cache writes that happen on a slightly-skewed clock.
    /// </remarks>
    private static string? SelectFreshestLocalSource(string cachePath, string bundlePath)
    {
        bool cacheExists = System.IO.File.Exists(cachePath);
        bool bundleExists = System.IO.File.Exists(bundlePath);

        // Implausibly-future cache mtimes are treated as "doesn't exist" — the
        // 5-minute tolerance absorbs ordinary NTP jitter; anything beyond that
        // is a clock-skew or restored-backup artefact and shouldn't outlive
        // the actual bundle. Bundle mtimes are not clamped because the bundle
        // is the floor — even an oddly-stamped bundle is still authoritative
        // when no valid cache exists.
        if (cacheExists)
        {
            DateTime cacheTime = System.IO.File.GetLastWriteTimeUtc(cachePath);
            if (cacheTime > DateTime.UtcNow.AddMinutes(5))
            {
                cacheExists = false;
            }
        }

        if (cacheExists && bundleExists)
        {
            DateTime cacheTime = System.IO.File.GetLastWriteTimeUtc(cachePath);
            DateTime bundleTime = System.IO.File.GetLastWriteTimeUtc(bundlePath);
            return cacheTime >= bundleTime ? cachePath : bundlePath;
        }

        if (cacheExists) { return cachePath; }
        if (bundleExists) { return bundlePath; }
        return null;
    }

    /// <summary>
    /// Computes the absolute path to the bundled manifest based on the running binary's
    /// directory. Internal for test access — the canonical layout is
    /// <c><see cref="AppContext.BaseDirectory"/>/share/winix/winix-manifest.json</c>;
    /// any refactor that changes the segments or their order would silently break the
    /// F1 offline-correctness contract on every released binary, hence the test pin.
    /// </summary>
    internal static string DefaultBundledPath()
    {
        return System.IO.Path.Combine(AppContext.BaseDirectory, "share", "winix", "winix-manifest.json");
    }

    /// <summary>
    /// Computes the absolute path to the per-user cache file. Returns
    /// <see cref="string.Empty"/> when no suitable cache root is available, so callers
    /// using this internally can compare with <see cref="System.IO.File.Exists(string)"/>
    /// (which returns <see langword="false"/> for empty strings) without further guards.
    /// </summary>
    private static string DefaultCachePath()
    {
        string root;
        if (OperatingSystem.IsWindows())
        {
            root = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? string.Empty;
        }
        else
        {
            string? xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (!string.IsNullOrEmpty(xdg))
            {
                root = xdg;
            }
            else
            {
                string? home = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetEnvironmentVariable("USERPROFILE");
                root = string.IsNullOrEmpty(home) ? string.Empty : System.IO.Path.Combine(home, ".cache");
            }
        }

        if (string.IsNullOrEmpty(root))
        {
            // No usable cache root — return an empty string so File.Exists returns false
            // and the resolution falls through to the bundle (or, ultimately, the network).
            return string.Empty;
        }

        return System.IO.Path.Combine(root, "winix", CacheFileName);
    }

    /// <summary>
    /// Downloads the manifest from the given URL and parses it. Used as the fallback path
    /// when no local source is present, and exposed publicly so tests and the refresh path
    /// can drive the network call directly.
    /// </summary>
    /// <param name="url">The URL to fetch the manifest from.</param>
    /// <exception cref="ManifestParseException">
    /// Thrown when the request fails, times out, or returns content that cannot be parsed.
    /// </exception>
    public static async Task<ToolManifest> LoadFromUrlAsync(string url)
    {
        string json = await FetchRawAsync(url).ConfigureAwait(false);
        return ToolManifest.Parse(json);
    }

    /// <summary>
    /// Fetches the raw JSON body from the given URL, wrapping framework exceptions in
    /// <see cref="ManifestParseException"/> with project-controlled messages. The 30-second
    /// timeout matches the cross-platform TLS-handshake-plus-redirect-chain budget for
    /// GitHub-hosted release assets.
    /// </summary>
    private static async Task<string> FetchRawAsync(string url)
    {
        using var client = new System.Net.Http.HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        try
        {
            return await client.GetStringAsync(url).ConfigureAwait(false);
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            // Don't pipe ex.Message — under InvariantGlobalization (AOT default) it
            // returns SR resource keys rather than localized English. Surface the
            // exception type so a user looking at logs has a discriminator without
            // exposing them to a raw resource key. See
            // feedback_invariant_globalization_resource_keys.md for the class context.
            throw new ManifestParseException(
                $"Failed to download manifest from '{url}' ({ex.GetType().Name}).", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new ManifestParseException(
                $"Timed out downloading manifest from '{url}'.", ex);
        }
    }
}
