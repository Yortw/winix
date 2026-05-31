using System.Net;
using Winix.HCat;
using Xunit;

namespace Winix.HCat.Tests;

public class QrRenderingTests
{
    [Fact]   // #1: the LAN QR was documented everywhere but the production caller passed null — it never rendered.
    public void Exposed_bind_renders_a_qr_block()
    {
        var bind = new BindInfo(IPAddress.Any, Exposed: true, new[] { "http://192.168.1.42:8080" });
        string? qr = HCatServer.RenderQr(bind);
        Assert.NotNull(qr);
        // Half-block glyphs the unicode renderer uses (full/upper/lower block) — proves a real QR, not blank.
        Assert.True(qr!.IndexOf('█') >= 0 || qr.IndexOf('▀') >= 0 || qr.IndexOf('▄') >= 0);
    }

    [Fact]
    public void Loopback_bind_renders_no_qr()
    {
        var bind = new BindInfo(IPAddress.Loopback, Exposed: false, new[] { "http://127.0.0.1:8080" });
        Assert.Null(HCatServer.RenderQr(bind));
    }

    [Fact]
    public void Exposed_bind_with_no_urls_renders_no_qr()
    {
        var bind = new BindInfo(IPAddress.Any, Exposed: true, new string[0]);
        Assert.Null(HCatServer.RenderQr(bind));
    }
}
