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

        public Task<bool> IsInstalled(string packageId) =>
            Task.FromResult(_installedPackages.Contains(packageId));

        public Task<string?> GetInstalledVersion(string packageId)
        {
            _versions.TryGetValue(packageId, out string? version);
            return Task.FromResult(version);
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
