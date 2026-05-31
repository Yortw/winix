using System.Net;
using Winix.HCat;
using Xunit;

namespace Winix.HCat.Tests;

public class LanAddressSelectorTests
{
    [Fact]   // Gateway present → only gateway-routed (LAN-reachable) addresses; virtual host-only IPs dropped.
    public void Returns_only_gateway_addresses_when_any_present()
    {
        var picked = HCatServer.SelectLanAddresses(new (string, bool)[]
        {
            ("172.20.80.1", false),   // Hyper-V vEthernet, no gateway
            ("192.168.1.84", true),   // physical Wi-Fi, has gateway
            ("172.28.80.1", false),
        });
        Assert.Equal(new[] { "192.168.1.84" }, picked);
    }

    [Fact]   // Order preserved among multiple gateway NICs (e.g. Ethernet + Wi-Fi both up).
    public void Preserves_order_among_gateway_addresses()
    {
        var picked = HCatServer.SelectLanAddresses(new (string, bool)[]
        {
            ("10.0.0.5", true),
            ("172.20.80.1", false),
            ("192.168.1.84", true),
        });
        Assert.Equal(new[] { "10.0.0.5", "192.168.1.84" }, picked);
    }

    [Fact]   // No gateway anywhere (isolated/static-IP LAN) → fall back to ALL addresses, nothing lost.
    public void Falls_back_to_all_when_none_have_gateway()
    {
        var picked = HCatServer.SelectLanAddresses(new (string, bool)[]
        {
            ("172.20.80.1", false),
            ("172.28.80.1", false),
        });
        Assert.Equal(new[] { "172.20.80.1", "172.28.80.1" }, picked);
    }

    [Fact]
    public void Empty_in_empty_out()
    {
        Assert.Empty(HCatServer.SelectLanAddresses(new (string, bool)[0]));
    }

    [Fact]   // F2: a real IPv4 gateway counts.
    public void HasUsableIPv4Gateway_true_for_real_ipv4_gateway()
    {
        Assert.True(HCatServer.HasUsableIPv4Gateway(new[] { IPAddress.Parse("192.168.1.254") }));
    }

    [Fact]   // F2: 0.0.0.0 is a placeholder gateway entry, NOT a real gateway — must not count.
    public void HasUsableIPv4Gateway_false_for_zero_placeholder_only()
    {
        Assert.False(HCatServer.HasUsableIPv4Gateway(new[] { IPAddress.Any }));
    }

    [Fact]   // F2: an IPv6 gateway does not make the NIC IPv4-LAN-reachable for our IPv4 URLs.
    public void HasUsableIPv4Gateway_false_for_ipv6_only()
    {
        Assert.False(HCatServer.HasUsableIPv4Gateway(new[] { IPAddress.Parse("fe80::1") }));
    }

    [Fact]   // F2: no gateway entries at all.
    public void HasUsableIPv4Gateway_false_for_empty()
    {
        Assert.False(HCatServer.HasUsableIPv4Gateway(System.Array.Empty<IPAddress>()));
    }

    [Fact]   // F2: a mix of placeholder + real → real one wins.
    public void HasUsableIPv4Gateway_true_when_real_mixed_with_placeholder()
    {
        Assert.True(HCatServer.HasUsableIPv4Gateway(
            new[] { IPAddress.Any, IPAddress.Parse("10.0.0.1") }));
    }
}
