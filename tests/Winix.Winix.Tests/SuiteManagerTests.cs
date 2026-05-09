#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Winix.Winix;

namespace Winix.Winix.Tests;

public class SuiteManagerTests
{
    private static ToolManifest CreateTestManifest()
    {
        string json = """
            {
              "version": "0.2.0",
              "tools": {
                "timeit": {
                  "description": "Time a command.",
                  "packages": { "winget": "Winix.TimeIt", "scoop": "timeit", "dotnet": "Winix.TimeIt" }
                },
                "squeeze": {
                  "description": "Compress files.",
                  "packages": { "winget": "Winix.Squeeze", "scoop": "squeeze", "dotnet": "Winix.Squeeze" }
                }
              }
            }
            """;
        return ToolManifest.Parse(json);
    }

    [Fact]
    public async Task InstallAll_AllSucceed_ReturnsZero()
    {
        var adapter = new FullFakeAdapter("winget", available: true, installExitCode: 0);
        var manager = new SuiteManager(CreateTestManifest(), adapter);

        var results = new List<string>();
        int exitCode = await manager.InstallAsync(null, dryRun: false, useColor: false, output: results.Add);

        Assert.Equal(0, exitCode);
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Contains("✓", r));
    }

    [Fact]
    public async Task InstallAll_OneFails_ReturnsOne()
    {
        int callCount = 0;
        Func<string, Task<ProcessResult>> installOverride = _ =>
        {
            callCount++;
            int exitCode = callCount == 2 ? 1 : 0;
            return Task.FromResult(new ProcessResult(exitCode, "", ""));
        };

        var adapter = new FullFakeAdapter("winget", available: true, installExitCode: 0, installOverride: installOverride);
        var manager = new SuiteManager(CreateTestManifest(), adapter);

        var results = new List<string>();
        int exitCode = await manager.InstallAsync(null, dryRun: false, useColor: false, output: results.Add);

        Assert.Equal(1, exitCode);
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Contains("✓"));
        Assert.Contains(results, r => r.Contains("✗"));
    }

    [Fact]
    public async Task InstallSpecificTools_OnlyInstallsNamed()
    {
        var adapter = new FullFakeAdapter("winget", available: true, installExitCode: 0);
        var manager = new SuiteManager(CreateTestManifest(), adapter);

        var results = new List<string>();
        int exitCode = await manager.InstallAsync(new[] { "timeit" }, dryRun: false, useColor: false, output: results.Add);

        Assert.Equal(0, exitCode);
        Assert.Single(results);
        Assert.Contains("timeit", results[0]);
    }

    [Fact]
    public async Task InstallDryRun_DoesNotCallAdapter()
    {
        var adapter = new FullFakeAdapter("winget", available: true, installExitCode: 0);
        var manager = new SuiteManager(CreateTestManifest(), adapter);

        var results = new List<string>();
        int exitCode = await manager.InstallAsync(null, dryRun: true, useColor: false, output: results.Add);

        Assert.Equal(0, exitCode);
        Assert.All(results, r => Assert.Contains("[dry-run]", r));
        Assert.Equal(0, adapter.InstallCallCount);
    }

    [Fact]
    public async Task ListAsync_ShowsAllTools()
    {
        var installedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Winix.TimeIt" };
        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "Winix.TimeIt", "0.2.0" } };
        var wingetAdapter = new FullFakeAdapter("winget", available: true, installExitCode: 0,
            installedPackages: installedPackages, versions: versions);
        var scoopAdapter = new FullFakeAdapter("scoop", available: false, installExitCode: 0);
        var dotnetAdapter = new FullFakeAdapter("dotnet", available: false, installExitCode: 0);

        var adapters = new Dictionary<string, IPackageManagerAdapter>(StringComparer.OrdinalIgnoreCase)
        {
            { "winget", wingetAdapter },
            { "scoop", scoopAdapter },
            { "dotnet", dotnetAdapter },
        };

        var manager = new SuiteManager(CreateTestManifest(), adapters, PlatformId.Windows);

        List<ToolStatus> statuses = await manager.ListAsync();

        Assert.Equal(2, statuses.Count);

        var timeitStatus = statuses.Find(s => s.Name == "timeit");
        Assert.NotNull(timeitStatus);
        Assert.True(timeitStatus.IsInstalled);
        Assert.Equal("0.2.0", timeitStatus.Version);

        var squeezeStatus = statuses.Find(s => s.Name == "squeeze");
        Assert.NotNull(squeezeStatus);
        Assert.False(squeezeStatus.IsInstalled);
    }

    [Fact]
    public async Task UninstallAll_NothingInstalled_ReturnsZeroWithNoOpSymbol()
    {
        // Idempotent contract (F5): uninstalling tools that aren't installed is a successful
        // no-op, not a failure. Each affected tool gets a ○ result instead of ✗, and the
        // overall exit code is 0 so CI cleanup workflows don't erroneously fail.
        var wingetAdapter = new FullFakeAdapter("winget", available: true, installExitCode: 0,
            installedPackages: new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var scoopAdapter = new FullFakeAdapter("scoop", available: false, installExitCode: 0);
        var dotnetAdapter = new FullFakeAdapter("dotnet", available: false, installExitCode: 0);

        var adapters = new Dictionary<string, IPackageManagerAdapter>(StringComparer.OrdinalIgnoreCase)
        {
            { "winget", wingetAdapter },
            { "scoop", scoopAdapter },
            { "dotnet", dotnetAdapter },
        };

        var manager = new SuiteManager(CreateTestManifest(), adapters, PlatformId.Windows);

        var results = new List<string>();
        int exitCode = await manager.UninstallAsync(null, dryRun: false, useColor: false, output: results.Add);

        Assert.Equal(WinixExitCode.Success, exitCode);
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Contains("○", r));
        Assert.All(results, r => Assert.Contains("not installed", r));
        Assert.All(results, r => Assert.DoesNotContain("✗", r));
    }

    [Fact]
    public async Task UninstallAsync_UsesBulkSnapshot_NotPerToolIsInstalled()
    {
        // Perf-fix contract: UninstallAsync must use a single GetInstalled() snapshot
        // per available adapter for ownership discovery, not per-tool IsInstalled().
        // Pre-fix this loop fired 22+ filtered subprocess calls per chain entry, taking
        // 5-7 minutes on real winget. Counting the fake's call counters proves the
        // bulk path is wired up — if a future refactor re-introduces per-tool IsInstalled,
        // this test fails immediately rather than waiting for a smoke-test wall-time
        // regression.
        var installedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Winix.TimeIt",
        };

        var wingetAdapter = new FullFakeAdapter("winget", available: true, installExitCode: 0,
            installedPackages: installedPackages);
        var scoopAdapter = new FullFakeAdapter("scoop", available: true, installExitCode: 0);
        var dotnetAdapter = new FullFakeAdapter("dotnet", available: false, installExitCode: 0);

        var adapters = new Dictionary<string, IPackageManagerAdapter>(StringComparer.OrdinalIgnoreCase)
        {
            { "winget", wingetAdapter },
            { "scoop", scoopAdapter },
            { "dotnet", dotnetAdapter },
        };

        var manager = new SuiteManager(CreateTestManifest(), adapters, PlatformId.Windows);

        var results = new List<string>();
        await manager.UninstallAsync(null, dryRun: true, useColor: false, output: results.Add);

        // Bulk path: GetInstalled fires once per AVAILABLE adapter; IsInstalled never fires.
        Assert.Equal(0, wingetAdapter.IsInstalledCallCount);
        Assert.Equal(0, scoopAdapter.IsInstalledCallCount);
        Assert.Equal(1, wingetAdapter.GetInstalledCallCount);
        Assert.Equal(1, scoopAdapter.GetInstalledCallCount);
        Assert.Equal(0, dotnetAdapter.GetInstalledCallCount);
    }

    [Fact]
    public async Task UninstallAll_AllSucceed_ReturnsZero()
    {
        var installedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Winix.TimeIt",
            "Winix.Squeeze",
        };

        var wingetAdapter = new FullFakeAdapter("winget", available: true, installExitCode: 0,
            installedPackages: installedPackages);
        var scoopAdapter = new FullFakeAdapter("scoop", available: false, installExitCode: 0);
        var dotnetAdapter = new FullFakeAdapter("dotnet", available: false, installExitCode: 0);

        var adapters = new Dictionary<string, IPackageManagerAdapter>(StringComparer.OrdinalIgnoreCase)
        {
            { "winget", wingetAdapter },
            { "scoop", scoopAdapter },
            { "dotnet", dotnetAdapter },
        };

        var manager = new SuiteManager(CreateTestManifest(), adapters, PlatformId.Windows);

        var results = new List<string>();
        int exitCode = await manager.UninstallAsync(null, dryRun: false, useColor: false, output: results.Add);

        Assert.Equal(0, exitCode);
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Contains("✓", r));
    }

    // ── End-to-end bulk-path tests with REAL adapter parsers ────────────────
    //
    // The FullFakeAdapter tests above prove the SuiteManager dispatch contract
    // (chain order, no-op vs failure semantics) but bypass each adapter's
    // tabular parser by deriving snapshots from a HashSet. The tests below
    // wire SuiteManager up to the real WingetAdapter / ScoopAdapter /
    // BrewAdapter / DotnetToolAdapter with a mocked process runner emitting
    // canned tabular output. This catches integration breakage between the
    // parsed snapshot keys and the manifest's per-PM package-id lookups.

    [Fact]
    public async Task ListAsync_RealWingetAdapter_RoundtripsTabularOutputToToolStatus()
    {
        // The realistic winget list output that the bulk path encounters:
        // includes the spinner prefix, 5-column "upgrade pending" shape, and
        // mixed-case Ids matching the manifest's published-case package IDs.
        const string wingetListOutput =
            "  -  \r\n" +
            "  \\ \r\n" +
            "Name           Id              Version  Available  Source\r\n" +
            "------------------------------------------------------------\r\n" +
            "Winix.TimeIt   Winix.TimeIt    0.2.0    0.4.0      winix\r\n";

        Task<ProcessResult> RunWinget(string command, string[] args)
        {
            return Task.FromResult(new ProcessResult(0, wingetListOutput, ""));
        }

        var realWinget = new WingetAdapter(RunWinget);
        var alwaysAvailableWinget = new ForceAvailableAdapter(realWinget);

        var adapters = new Dictionary<string, IPackageManagerAdapter>(StringComparer.OrdinalIgnoreCase)
        {
            { "winget", alwaysAvailableWinget },
        };

        var manager = new SuiteManager(CreateTestManifest(), adapters, PlatformId.Windows);

        List<ToolStatus> statuses = await manager.ListAsync();

        ToolStatus? timeitStatus = statuses.Find(s => s.Name == "timeit");
        Assert.NotNull(timeitStatus);
        Assert.True(timeitStatus.IsInstalled);
        Assert.Equal("0.2.0", timeitStatus.Version);
        Assert.Equal("winget", timeitStatus.PackageManager);

        // squeeze isn't in the winget list output, so it must surface as
        // not-installed without throwing or swallowing an error.
        ToolStatus? squeezeStatus = statuses.Find(s => s.Name == "squeeze");
        Assert.NotNull(squeezeStatus);
        Assert.False(squeezeStatus.IsInstalled);
    }

    [Fact]
    public async Task ListAsync_RealScoopAdapter_RoundtripsTabularOutputToToolStatus()
    {
        // scoop's tabular output uses a different preamble ("Installed apps:")
        // and column set (Name | Version | Source) — verifying the bulk-snapshot
        // contract holds across PM-specific output shapes.
        const string scoopListOutput =
            "Installed apps:\r\n" +
            "\r\n" +
            "  Name    Version Source\r\n" +
            "  ----    ------- ------\r\n" +
            "  timeit  0.2.0   winix\r\n" +
            "  squeeze 0.1.5   winix\r\n";

        Task<ProcessResult> RunScoop(string command, string[] args)
        {
            return Task.FromResult(new ProcessResult(0, scoopListOutput, ""));
        }

        var realScoop = new ScoopAdapter(RunScoop);
        var alwaysAvailableScoop = new ForceAvailableAdapter(realScoop);

        var adapters = new Dictionary<string, IPackageManagerAdapter>(StringComparer.OrdinalIgnoreCase)
        {
            // winget unavailable; scoop is the resolved adapter.
            { "winget", new FullFakeAdapter("winget", available: false, installExitCode: 0) },
            { "scoop", alwaysAvailableScoop },
        };

        var manager = new SuiteManager(CreateTestManifest(), adapters, PlatformId.Windows);

        List<ToolStatus> statuses = await manager.ListAsync();

        ToolStatus? timeitStatus = statuses.Find(s => s.Name == "timeit");
        Assert.NotNull(timeitStatus);
        Assert.True(timeitStatus.IsInstalled);
        Assert.Equal("0.2.0", timeitStatus.Version);
        Assert.Equal("scoop", timeitStatus.PackageManager);

        ToolStatus? squeezeStatus = statuses.Find(s => s.Name == "squeeze");
        Assert.NotNull(squeezeStatus);
        Assert.True(squeezeStatus.IsInstalled);
        Assert.Equal("0.1.5", squeezeStatus.Version);
        Assert.Equal("scoop", squeezeStatus.PackageManager);
    }

    [Fact]
    public async Task ListAsync_RealDotnetToolAdapter_BridgesLowercaseIdToManifestPublishedCase()
    {
        // dotnet tool list -g lowercases every id; the manifest stores
        // "Winix.TimeIt" (published case). The case-insensitive snapshot key
        // bridges this — without it, a perfectly-installed dotnet global tool
        // would surface as "not installed".
        const string dotnetListOutput =
            "Package Id      Version    Commands\r\n" +
            "----------------------------------------\r\n" +
            "winix.timeit    0.2.0      timeit\r\n";

        Task<ProcessResult> RunDotnet(string command, string[] args)
        {
            return Task.FromResult(new ProcessResult(0, dotnetListOutput, ""));
        }

        var realDotnet = new DotnetToolAdapter(RunDotnet);
        var alwaysAvailableDotnet = new ForceAvailableAdapter(realDotnet);

        var adapters = new Dictionary<string, IPackageManagerAdapter>(StringComparer.OrdinalIgnoreCase)
        {
            { "winget", new FullFakeAdapter("winget", available: false, installExitCode: 0) },
            { "scoop", new FullFakeAdapter("scoop", available: false, installExitCode: 0) },
            { "dotnet", alwaysAvailableDotnet },
        };

        var manager = new SuiteManager(CreateTestManifest(), adapters, PlatformId.Windows);

        List<ToolStatus> statuses = await manager.ListAsync();

        ToolStatus? timeitStatus = statuses.Find(s => s.Name == "timeit");
        Assert.NotNull(timeitStatus);
        Assert.True(timeitStatus.IsInstalled);
        Assert.Equal("0.2.0", timeitStatus.Version);
        Assert.Equal("dotnet", timeitStatus.PackageManager);
    }

    [Fact]
    public async Task ListAsync_RealBrewAdapter_RoundtripsLineFormatToToolStatus()
    {
        // brew uses a different shape entirely — one line per formula, no
        // header, no fixed-width columns. The bulk-path interface is uniform
        // even though the parser implementations differ wildly.
        const string brewManifestJson = """
            {
              "version": "0.2.0",
              "tools": {
                "timeit": {
                  "description": "Time a command.",
                  "packages": { "brew": "timeit", "dotnet": "Winix.TimeIt" }
                },
                "squeeze": {
                  "description": "Compress files.",
                  "packages": { "brew": "squeeze", "dotnet": "Winix.Squeeze" }
                }
              }
            }
            """;

        const string brewListOutput =
            "timeit 0.2.0\n" +
            "squeeze 0.1.5\n";

        Task<ProcessResult> RunBrew(string command, string[] args)
        {
            return Task.FromResult(new ProcessResult(0, brewListOutput, ""));
        }

        var realBrew = new BrewAdapter(RunBrew);
        var alwaysAvailableBrew = new ForceAvailableAdapter(realBrew);

        var adapters = new Dictionary<string, IPackageManagerAdapter>(StringComparer.OrdinalIgnoreCase)
        {
            { "brew", alwaysAvailableBrew },
            { "dotnet", new FullFakeAdapter("dotnet", available: false, installExitCode: 0) },
        };

        var manager = new SuiteManager(ToolManifest.Parse(brewManifestJson), adapters, PlatformId.MacOS);

        List<ToolStatus> statuses = await manager.ListAsync();

        ToolStatus? timeitStatus = statuses.Find(s => s.Name == "timeit");
        Assert.NotNull(timeitStatus);
        Assert.True(timeitStatus.IsInstalled);
        Assert.Equal("0.2.0", timeitStatus.Version);
        Assert.Equal("brew", timeitStatus.PackageManager);
    }

    [Fact]
    public async Task ListAsync_TwoAvailablePms_FirstInChainWins()
    {
        // Both winget and scoop are available, both report timeit installed.
        // The Windows chain is (winget, scoop, dotnet), so winget wins. This
        // is a regression test for the chain-order-preserved-under-bulk-path
        // invariant — pre-bulk the chain ordering was implicit in the loop
        // structure, and an early bulk implementation accidentally walked
        // the snapshot dictionary in iteration order rather than chain order.
        const string wingetListOutput =
            "Name         Id              Version\r\n" +
            "----------------------------------------\r\n" +
            "Winix.TimeIt Winix.TimeIt    0.4.0\r\n";

        const string scoopListOutput =
            "  Name   Version Source\r\n" +
            "  ----   ------- ------\r\n" +
            "  timeit 0.3.0   winix\r\n";

        var winget = new ForceAvailableAdapter(new WingetAdapter(
            (cmd, args) => Task.FromResult(new ProcessResult(0, wingetListOutput, ""))));
        var scoop = new ForceAvailableAdapter(new ScoopAdapter(
            (cmd, args) => Task.FromResult(new ProcessResult(0, scoopListOutput, ""))));

        var adapters = new Dictionary<string, IPackageManagerAdapter>(StringComparer.OrdinalIgnoreCase)
        {
            { "winget", winget },
            { "scoop", scoop },
        };

        var manager = new SuiteManager(CreateTestManifest(), adapters, PlatformId.Windows);

        List<ToolStatus> statuses = await manager.ListAsync();

        ToolStatus? timeitStatus = statuses.Find(s => s.Name == "timeit");
        Assert.NotNull(timeitStatus);
        Assert.True(timeitStatus.IsInstalled);
        Assert.Equal("0.4.0", timeitStatus.Version);
        Assert.Equal("winget", timeitStatus.PackageManager);
    }

    /// <summary>
    /// Wraps a real adapter and forces <see cref="IsAvailable"/> to return true,
    /// so SuiteManager-level tests don't depend on the dev/CI machine actually
    /// having winget/scoop/brew/dotnet on PATH. All other calls delegate to the
    /// real adapter, including its parser and process-running pipeline.
    /// </summary>
    private sealed class ForceAvailableAdapter : IPackageManagerAdapter
    {
        private readonly IPackageManagerAdapter _inner;

        public ForceAvailableAdapter(IPackageManagerAdapter inner)
        {
            _inner = inner;
        }

        public string Name => _inner.Name;

        public bool IsAvailable() => true;

        public Task<bool> IsInstalled(string packageId) => _inner.IsInstalled(packageId);

        public Task<string?> GetInstalledVersion(string packageId) => _inner.GetInstalledVersion(packageId);

        public Task<IReadOnlyDictionary<string, string?>> GetInstalled() => _inner.GetInstalled();

        public Task<ProcessResult> Install(string packageId) => _inner.Install(packageId);

        public Task<ProcessResult> Update(string packageId) => _inner.Update(packageId);

        public Task<ProcessResult> Uninstall(string packageId) => _inner.Uninstall(packageId);
    }

    // -----------------------------------------------------------------------
    // Fake adapter used by all tests above.
    // -----------------------------------------------------------------------

    private sealed class FullFakeAdapter : IPackageManagerAdapter
    {
        private readonly bool _available;
        private readonly int _installExitCode;
        private readonly Func<string, Task<ProcessResult>>? _installOverride;
        private readonly HashSet<string> _installedPackages;
        private readonly Dictionary<string, string> _versions;

        public string Name { get; }
        public int InstallCallCount { get; private set; }

        // Track per-package IsInstalled calls so tests can assert that the
        // bulk-snapshot paths (ListAsync, UninstallAsync) never fall back to
        // the slow per-package probe.
        public int IsInstalledCallCount { get; private set; }
        public int GetInstalledCallCount { get; private set; }

        public FullFakeAdapter(
            string name, bool available, int installExitCode,
            Func<string, Task<ProcessResult>>? installOverride = null,
            HashSet<string>? installedPackages = null,
            Dictionary<string, string>? versions = null)
        {
            Name = name;
            _available = available;
            _installExitCode = installExitCode;
            _installOverride = installOverride;
            _installedPackages = installedPackages ?? new HashSet<string>();
            _versions = versions ?? new Dictionary<string, string>();
        }

        public bool IsAvailable() => _available;

        public Task<bool> IsInstalled(string packageId)
        {
            IsInstalledCallCount++;
            return Task.FromResult(_installedPackages.Contains(packageId));
        }

        public Task<string?> GetInstalledVersion(string packageId)
        {
            _versions.TryGetValue(packageId, out string? version);
            return Task.FromResult(version);
        }

        public Task<IReadOnlyDictionary<string, string?>> GetInstalled()
        {
            GetInstalledCallCount++;
            // Build a snapshot from the installed-packages set, supplying the version
            // from _versions when present so list/status tests get the same shape they
            // expected from the per-package GetInstalledVersion path.
            var snapshot = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (string pkg in _installedPackages)
            {
                _versions.TryGetValue(pkg, out string? version);
                snapshot[pkg] = version;
            }
            return Task.FromResult<IReadOnlyDictionary<string, string?>>(snapshot);
        }

        public Task<ProcessResult> Install(string packageId)
        {
            InstallCallCount++;
            if (_installOverride is not null)
            {
                return _installOverride(packageId);
            }

            return Task.FromResult(new ProcessResult(_installExitCode, "", ""));
        }

        public Task<ProcessResult> Update(string packageId) =>
            Task.FromResult(new ProcessResult(_installExitCode, "", ""));

        public Task<ProcessResult> Uninstall(string packageId) =>
            Task.FromResult(new ProcessResult(0, "", ""));
    }
}
