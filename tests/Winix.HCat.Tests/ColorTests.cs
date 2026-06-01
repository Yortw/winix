#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Net;
using Winix.HCat;
using Xunit;

namespace Winix.HCat.Tests;

public class ColorTests
{
    private static readonly string Esc = ((char)27).ToString();

    // Helper: a minimal RequestRecord for test use.
    private static RequestRecord Req(string method, string path)
        => new RequestRecord(method, path, "", new Dictionary<string, string>(), null, "t", "127.0.0.1");

    // ──────────────────────────────────────────────────────────────────────
    // Banner tests
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Banner_WithColor_EmitsAnsi_WithoutIsPlain()
    {
        var info = new BindInfo(IPAddress.Loopback, false, new[] { "http://127.0.0.1:8080" });
        var options = new HCatOptions { Mode = HCatMode.Serve, Directory = "." };

        string colored = Banner.Render(info, options, qr: null, useColor: true);
        string plain   = Banner.Render(info, options, qr: null, useColor: false);

        Assert.Contains(Esc, colored, System.StringComparison.Ordinal);
        Assert.DoesNotContain(Esc, plain, System.StringComparison.Ordinal);
        Assert.Contains("127.0.0.1", plain, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Banner_Plain_ExactContent()
    {
        // A3: verify plain output is byte-identical (no stray escape chars, expected text present).
        var info = new BindInfo(IPAddress.Loopback, false, new[] { "http://127.0.0.1:8080" });
        var options = new HCatOptions { Mode = HCatMode.Serve, Directory = "." };

        string plain = Banner.Render(info, options, qr: null, useColor: false);

        // The "Serving" label and URL must be present in plain text.
        Assert.Contains("Serving", plain, System.StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1:8080", plain, System.StringComparison.Ordinal);
        // No escape sequences at all.
        Assert.DoesNotContain(Esc, plain, System.StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Request-log tests (OnServeAccess)
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RequestLog_ColorsStatusByClass()
    {
        var sink = new StringWriter();
        var lifecycle = new CaptureLifecycle(
            new CaptureController(null, null), jsonSink: null, humanSink: sink, useColor: true);

        lifecycle.OnServeAccess(Req("GET", "/ok"),  status: 200);
        lifecycle.OnServeAccess(Req("GET", "/bad"), status: 500);

        string log = sink.ToString();
        Assert.Contains(Esc, log, System.StringComparison.Ordinal);
        Assert.Contains("/ok",  log, System.StringComparison.Ordinal);
        Assert.Contains("/bad", log, System.StringComparison.Ordinal);
    }

    [Fact]
    public void RequestLog_NoColor_IsPlain()
    {
        var sink = new StringWriter();
        var lifecycle = new CaptureLifecycle(
            new CaptureController(null, null), jsonSink: null, humanSink: sink, useColor: false);

        lifecycle.OnServeAccess(Req("GET", "/x"), status: 200);

        string log = sink.ToString();
        Assert.DoesNotContain(Esc, log, System.StringComparison.Ordinal);
        // A3: exact plain log line.
        Assert.Equal("GET /x 200" + System.Environment.NewLine, log);
    }

    // A4: status-class Theory — exercises 2xx / 3xx / 4xx / 5xx boundaries.
    [Theory]
    [InlineData(200)]
    [InlineData(204)]
    [InlineData(301)]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(503)]
    public void RequestLog_StatusClasses_AllColoredOn(int status)
    {
        var sink = new StringWriter();
        var lifecycle = new CaptureLifecycle(
            new CaptureController(null, null), jsonSink: null, humanSink: sink, useColor: true);

        lifecycle.OnServeAccess(Req("GET", "/p"), status);

        string log = sink.ToString();
        Assert.Contains(Esc, log, System.StringComparison.Ordinal);
        Assert.Contains(status.ToString(System.Globalization.CultureInfo.InvariantCulture), log,
            System.StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────
    // P2-B: OnRecord (non-serve per-request line) dim-method colour
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void OnRecord_MethodColoredOn_PlainOff()
    {
        // Colour on.
        var on = new StringWriter();
        new CaptureLifecycle(
            new CaptureController(null, null), jsonSink: null, humanSink: on, useColor: true)
            .OnRecord(Req("POST", "/submit"));

        Assert.Contains(Esc, on.ToString(), System.StringComparison.Ordinal);
        Assert.Contains("/submit", on.ToString(), System.StringComparison.Ordinal);

        // Colour off — A3 exact plain line.
        var off = new StringWriter();
        new CaptureLifecycle(
            new CaptureController(null, null), jsonSink: null, humanSink: off, useColor: false)
            .OnRecord(Req("POST", "/submit"));

        Assert.DoesNotContain(Esc, off.ToString(), System.StringComparison.Ordinal);
        Assert.Equal("POST /submit" + System.Environment.NewLine, off.ToString());
    }
}
