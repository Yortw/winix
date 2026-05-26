#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;
using Winix.Notify;

namespace Winix.Notify.Tests;

public class FormattingTests
{
    [Fact]
    public void Json_TitleAndBody_AppearAtTopLevel()
    {
        var opts = NotifyOptions.ForTests("the title") with { Body = "the body" };
        var results = new List<BackendResult>
        {
            new("windows-toast", true, null, null),
        };
        string json = Formatting.Json(opts, results);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("the title", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal("the body", doc.RootElement.GetProperty("body").GetString());
    }

    [Fact]
    public void Json_Urgency_LowerCaseString()
    {
        var opts = NotifyOptions.ForTests("t") with { Urgency = Urgency.Critical };
        var results = new List<BackendResult>();
        string json = Formatting.Json(opts, results);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("critical", doc.RootElement.GetProperty("urgency").GetString());
    }

    [Fact]
    public void Json_BackendsArray_OrderPreservedFromInput()
    {
        var opts = NotifyOptions.ForTests("t");
        var results = new List<BackendResult>
        {
            new("first", true, null, null),
            new("second", true, null, null),
        };
        string json = Formatting.Json(opts, results);
        using var doc = JsonDocument.Parse(json);
        var backends = doc.RootElement.GetProperty("backends").EnumerateArray().ToArray();
        Assert.Equal(2, backends.Length);
        Assert.Equal("first", backends[0].GetProperty("name").GetString());
        Assert.Equal("second", backends[1].GetProperty("name").GetString());
    }

    [Fact]
    public void Json_FailedBackend_IncludesError()
    {
        var opts = NotifyOptions.ForTests("t");
        var results = new List<BackendResult>
        {
            new("ntfy", false, "topic not found", null),
        };
        string json = Formatting.Json(opts, results);
        using var doc = JsonDocument.Parse(json);
        var b = doc.RootElement.GetProperty("backends")[0];
        Assert.False(b.GetProperty("ok").GetBoolean());
        Assert.Equal("topic not found", b.GetProperty("error").GetString());
    }

    [Fact]
    public void Json_BackendDetail_AppearsInline()
    {
        var opts = NotifyOptions.ForTests("t");
        var detail = new Dictionary<string, string>
        {
            ["server"] = "https://ntfy.sh",
            ["topic"] = "alerts",
        };
        var results = new List<BackendResult>
        {
            new("ntfy", true, null, detail),
        };
        string json = Formatting.Json(opts, results);
        using var doc = JsonDocument.Parse(json);
        var b = doc.RootElement.GetProperty("backends")[0];
        Assert.Equal("https://ntfy.sh", b.GetProperty("server").GetString());
        Assert.Equal("alerts", b.GetProperty("topic").GetString());
    }

    [Fact]
    public void Json_NullBody_OmittedFromOutput()
    {
        var opts = NotifyOptions.ForTests("t");  // Body is null by default
        var results = new List<BackendResult>();
        string json = Formatting.Json(opts, results);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("body", out _));
    }
}
