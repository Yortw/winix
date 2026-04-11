#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Winix.Winix;

namespace Winix.Winix.Tests;

public class PlatformDetectorTests
{
    // ── GetDefaultChain ───────────────────────────────────────────────────────

    [Fact]
    public void GetDefaultChain_Windows_ReturnsWingetScoopDotnet()
    {
        string[] chain = PlatformDetector.GetDefaultChain(PlatformId.Windows);

        Assert.Equal(new[] { "winget", "scoop", "dotnet" }, chain);
    }

    [Fact]
    public void GetDefaultChain_MacOS_ReturnsBrewDotnet()
    {
        string[] chain = PlatformDetector.GetDefaultChain(PlatformId.MacOS);

        Assert.Equal(new[] { "brew", "dotnet" }, chain);
    }

    [Fact]
    public void GetDefaultChain_Linux_ReturnsDotnet()
    {
        string[] chain = PlatformDetector.GetDefaultChain(PlatformId.Linux);

        Assert.Equal(new[] { "dotnet" }, chain);
    }

    // ── ResolveAdapter ────────────────────────────────────────────────────────

    [Fact]
    public void ResolveAdapter_ViaOverride_ReturnsSpecifiedPm()
    {
        // Both winget and scoop are available; override selects scoop explicitly.
        var adapters = new Dictionary<string, IPackageManagerAdapter>
        {
            ["winget"] = new FakeAdapter("winget", available: true),
            ["scoop"]  = new FakeAdapter("scoop",  available: true),
        };

        IPackageManagerAdapter? result = PlatformDetector.ResolveAdapter("scoop", adapters, PlatformId.Windows);

        Assert.NotNull(result);
        Assert.Equal("scoop", result.Name);
    }

    [Fact]
    public void ResolveAdapter_ViaOverrideNotAvailable_ReturnsNull()
    {
        // Override requests scoop but scoop is not available.
        var adapters = new Dictionary<string, IPackageManagerAdapter>
        {
            ["winget"] = new FakeAdapter("winget", available: true),
            ["scoop"]  = new FakeAdapter("scoop",  available: false),
        };

        IPackageManagerAdapter? result = PlatformDetector.ResolveAdapter("scoop", adapters, PlatformId.Windows);

        Assert.Null(result);
    }

    [Fact]
    public void ResolveAdapter_NoOverride_ReturnsFirstAvailable()
    {
        // No override; walk the Windows chain (winget → scoop → dotnet).
        // winget unavailable, scoop available → expect scoop.
        var adapters = new Dictionary<string, IPackageManagerAdapter>
        {
            ["winget"] = new FakeAdapter("winget", available: false),
            ["scoop"]  = new FakeAdapter("scoop",  available: true),
            ["dotnet"] = new FakeAdapter("dotnet", available: true),
        };

        IPackageManagerAdapter? result = PlatformDetector.ResolveAdapter(null, adapters, PlatformId.Windows);

        Assert.NotNull(result);
        Assert.Equal("scoop", result.Name);
    }

    [Fact]
    public void ResolveAdapter_NoneAvailable_ReturnsNull()
    {
        var adapters = new Dictionary<string, IPackageManagerAdapter>
        {
            ["winget"] = new FakeAdapter("winget", available: false),
            ["scoop"]  = new FakeAdapter("scoop",  available: false),
            ["dotnet"] = new FakeAdapter("dotnet", available: false),
        };

        IPackageManagerAdapter? result = PlatformDetector.ResolveAdapter(null, adapters, PlatformId.Windows);

        Assert.Null(result);
    }

    // ── GetCurrentPlatform ────────────────────────────────────────────────────

    [Fact]
    public void GetCurrentPlatform_ReturnsValidValue()
    {
        PlatformId platform = PlatformDetector.GetCurrentPlatform();

        Assert.True(
            platform is PlatformId.Windows or PlatformId.MacOS or PlatformId.Linux,
            $"Unexpected PlatformId value: {platform}");
    }

    // ── Test double ───────────────────────────────────────────────────────────

    private sealed class FakeAdapter : IPackageManagerAdapter
    {
        private readonly bool _available;

        public string Name { get; }

        public FakeAdapter(string name, bool available)
        {
            Name = name;
            _available = available;
        }

        public bool IsAvailable() => _available;

        public Task<bool> IsInstalled(string packageId) => Task.FromResult(false);

        public Task<string?> GetInstalledVersion(string packageId) => Task.FromResult<string?>(null);

        public Task<ProcessResult> Install(string packageId) => Task.FromResult(new ProcessResult(0, "", ""));

        public Task<ProcessResult> Update(string packageId) => Task.FromResult(new ProcessResult(0, "", ""));

        public Task<ProcessResult> Uninstall(string packageId) => Task.FromResult(new ProcessResult(0, "", ""));
    }
}
