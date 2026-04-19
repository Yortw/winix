#nullable enable
using System.Text.Json;
using Xunit;
using Winix.Url;

namespace Winix.Url.Tests;

public class FormattingTests
{
    private static ParsedUrl Sample() => new(
        Scheme: "https",
        UserInfo: null,
        Host: "api.example.com",
        Port: 8443,
        Path: "/v1/users",
        QueryPairs: new (string, string)[] { ("q", "hello world"), ("limit", "10") },
        Fragment: "top");

    [Fact]
    public void PlainText_EmitsKeyValueLines()
    {
        string output = Formatting.PlainText(Sample());
        Assert.Contains("scheme=https", output);
        Assert.Contains("host=api.example.com", output);
        Assert.Contains("port=8443", output);
        Assert.Contains("path=/v1/users", output);
        Assert.Contains("query=q=hello+world&limit=10", output);
        Assert.Contains("fragment=top", output);
    }

    [Fact]
    public void PlainText_NullFields_OmittedFromOutput()
    {
        var p = new ParsedUrl("https", null, "x.io", null, "/", System.Array.Empty<(string, string)>(), null);
        string output = Formatting.PlainText(p);
        Assert.DoesNotContain("userinfo=", output);
        Assert.DoesNotContain("port=", output);
        Assert.DoesNotContain("fragment=", output);
        Assert.DoesNotContain("query=", output);
    }

    [Fact]
    public void Field_Host_ReturnsHost()
    {
        Assert.Equal("api.example.com", Formatting.Field(Sample(), "host"));
    }

    [Fact]
    public void Field_Port_ReturnsPortAsString()
    {
        Assert.Equal("8443", Formatting.Field(Sample(), "port"));
    }

    [Fact]
    public void Field_Query_ReturnsRawFormEncodedQueryString()
    {
        Assert.Equal("q=hello+world&limit=10", Formatting.Field(Sample(), "query"));
    }

    [Fact]
    public void Field_UnknownField_Throws()
    {
        Assert.Throws<System.ArgumentException>(() => Formatting.Field(Sample(), "bogus"));
    }

    [Fact]
    public void Field_NullField_ReturnsEmpty()
    {
        var p = new ParsedUrl("https", null, "x.io", null, "/", System.Array.Empty<(string, string)>(), null);
        Assert.Equal("", Formatting.Field(p, "fragment"));
    }

    [Fact]
    public void Json_HasExpectedShape()
    {
        string json = Formatting.Json(Sample());
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("https", root.GetProperty("scheme").GetString());
        Assert.Equal("api.example.com", root.GetProperty("host").GetString());
        Assert.Equal(8443, root.GetProperty("port").GetInt32());
        Assert.Equal("/v1/users", root.GetProperty("path").GetString());
        Assert.Equal("top", root.GetProperty("fragment").GetString());

        var q = root.GetProperty("query");
        Assert.Equal(2, q.GetArrayLength());
        Assert.Equal("q", q[0].GetProperty("key").GetString());
        Assert.Equal("hello world", q[0].GetProperty("value").GetString());
    }

    [Fact]
    public void Json_NullFields_SerialisedAsNull()
    {
        var p = new ParsedUrl("https", null, "x.io", null, "/", System.Array.Empty<(string, string)>(), null);
        string json = Formatting.Json(p);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("userinfo").ValueKind);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("port").ValueKind);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("fragment").ValueKind);
    }
}
