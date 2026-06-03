using Winix.MkAuth;
using Xunit;

public class FormattingTests
{
    private static readonly HeaderResult Sample = new("Authorization", "Bearer xyz", BaseString: "BASE");

    [Fact]
    public void Plain_emits_full_header_line()
        => Assert.Equal("Authorization: Bearer xyz", Formatting.Plain(Sample, valueOnly: false));

    [Fact]
    public void Value_only_emits_bare_value()
        => Assert.Equal("Bearer xyz", Formatting.Plain(Sample, valueOnly: true));

    [Fact]
    public void Json_envelope_shape()
    {
        string json = Formatting.Json(new HeaderResult("Authorization", "OAuth abc"), scheme: "oauth1", includeBaseString: false);
        Assert.Contains("\"scheme\":\"oauth1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"header_name\":\"Authorization\"", json, StringComparison.Ordinal);
        Assert.Contains("\"header_value\":\"OAuth abc\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("base_string", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_includes_base_string_when_requested()
    {
        string json = Formatting.Json(Sample, scheme: "jwt", includeBaseString: true);
        Assert.Contains("\"base_string\":\"BASE\"", json, StringComparison.Ordinal);
    }
}
