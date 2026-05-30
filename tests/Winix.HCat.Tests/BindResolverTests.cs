using System.Collections.Generic;
using System.Net;
using Winix.HCat;
using Xunit;

namespace Winix.HCat.Tests;

public class BindResolverTests
{
    private static IReadOnlyList<string> FakeLanIps() => new[] { "192.168.1.42" };

    [Fact]
    public void Default_is_loopback_not_exposed()
    {
        var info = BindResolver.Resolve(new HCatOptions { Port = 8080 }, FakeLanIps);
        Assert.Equal(IPAddress.Loopback, info.Address);
        Assert.False(info.Exposed);
        Assert.Contains("http://127.0.0.1:8080", info.Urls);
    }

    [Fact]
    public void Lan_binds_any_and_is_exposed_with_lan_urls()
    {
        var info = BindResolver.Resolve(new HCatOptions { Lan = true, Port = 9000 }, FakeLanIps);
        Assert.Equal(IPAddress.Any, info.Address);
        Assert.True(info.Exposed);
        Assert.Contains("http://192.168.1.42:9000", info.Urls);
    }

    [Fact]
    public void Explicit_nonloopback_host_is_exposed()
    {
        var info = BindResolver.Resolve(new HCatOptions { Host = "0.0.0.0", Port = 8080 }, FakeLanIps);
        Assert.True(info.Exposed);
    }

    [Fact]
    public void Explicit_loopback_host_is_not_exposed()
    {
        var info = BindResolver.Resolve(new HCatOptions { Host = "127.0.0.1", Port = 8080 }, FakeLanIps);
        Assert.False(info.Exposed);
    }

    [Fact]
    public void Https_scheme_is_used_when_enabled()
    {
        var info = BindResolver.Resolve(new HCatOptions { Https = true, Port = 8443 }, FakeLanIps);
        Assert.Contains("https://127.0.0.1:8443", info.Urls);
    }
}
