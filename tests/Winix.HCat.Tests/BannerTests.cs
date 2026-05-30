using System.Collections.Generic;
using System.Net;
using Winix.HCat;
using Xunit;

namespace Winix.HCat.Tests;

public class BannerTests
{
    [Fact]
    public void Loopback_banner_hints_lan_and_shows_no_qr()
    {
        var info = new BindInfo(IPAddress.Loopback, false, new[] { "http://127.0.0.1:8080" });
        string text = Banner.Render(info, new HCatOptions(), qr: null);
        Assert.Contains("http://127.0.0.1:8080", text);
        Assert.Contains("--lan", text);   // the "pass --lan to share" hint
    }

    [Fact]
    public void Exposed_upload_into_served_root_warns()
    {
        var info = new BindInfo(IPAddress.Any, true, new[] { "http://192.168.1.42:8080" });
        var opts = new HCatOptions { Mode = HCatMode.Serve, Upload = true, UploadDir = ".", Directory = "." };
        string text = Banner.Render(info, opts, qr: "QRBLOCK");
        Assert.Contains("downloadable", text);   // the served-root upload warning
        Assert.Contains("QRBLOCK", text);
    }
}
