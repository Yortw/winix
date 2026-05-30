using System.Collections.Generic;
using Winix.HCat;
using Xunit;

namespace Winix.HCat.Tests;

public class CaptureControllerTests
{
    private static RequestRecord Req(string method, string path, string? body = null)
        => new(method, path, "", new Dictionary<string, string>(), body, "t", "127.0.0.1");

    [Fact]
    public void Capture_count_stops_after_n()
    {
        var c = new CaptureController(captureCount: 2, exitOn: null);
        Assert.False(c.OnRequest(Req("GET", "/")));
        Assert.True(c.OnRequest(Req("GET", "/")));   // 2nd → stop
    }

    [Fact]
    public void Exit_on_path_matches()
    {
        var c = new CaptureController(captureCount: null, exitOn: "path=/done");
        Assert.False(c.OnRequest(Req("GET", "/")));
        Assert.True(c.OnRequest(Req("GET", "/done")));
    }

    [Fact]
    public void Exit_on_method_matches()
    {
        var c = new CaptureController(captureCount: null, exitOn: "method=POST");
        Assert.False(c.OnRequest(Req("GET", "/")));
        Assert.True(c.OnRequest(Req("POST", "/")));
    }

    [Fact]
    public void Exit_on_body_substring_matches()
    {
        var c = new CaptureController(captureCount: null, exitOn: "body~ok");
        Assert.False(c.OnRequest(Req("POST", "/", "nope")));
        Assert.True(c.OnRequest(Req("POST", "/", "all ok here")));
    }

    [Fact]
    public void No_condition_never_stops()
    {
        var c = new CaptureController(captureCount: null, exitOn: null);
        Assert.False(c.OnRequest(Req("GET", "/")));
        Assert.False(c.OnRequest(Req("GET", "/")));
    }
}
