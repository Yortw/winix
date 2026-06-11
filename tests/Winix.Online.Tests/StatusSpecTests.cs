#nullable enable

using Winix.Online;
using Xunit;

namespace Winix.Online.Tests;

public class StatusSpecTests
{
    [Fact]
    public void Default_matches_2xx_only()
    {
        StatusSpec spec = StatusSpec.Default;
        Assert.True(spec.Matches(200));
        Assert.True(spec.Matches(204));
        Assert.True(spec.Matches(299));
        Assert.False(spec.Matches(199));
        Assert.False(spec.Matches(300));
        Assert.False(spec.Matches(503));
    }

    [Theory]
    [InlineData("2xx", 204, true)]
    [InlineData("2xx", 404, false)]
    [InlineData("200,204", 204, true)]
    [InlineData("200,204", 201, false)]
    [InlineData("200-299", 250, true)]
    [InlineData("200-299", 300, false)]
    [InlineData("200,500-599", 503, true)]   // mixed list + range
    [InlineData("5xx", 503, true)]
    [InlineData("5xx", 200, false)]
    [InlineData("2XX", 204, true)]           // F7: uppercase class shorthand accepted
    [InlineData("200,", 200, true)]          // F7: trailing comma tolerated (RemoveEmptyEntries)
    public void Parse_then_match(string spec, int code, bool expected)
    {
        Assert.True(StatusSpec.TryParse(spec, out StatusSpec parsed, out _));
        Assert.Equal(expected, parsed.Matches(code));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("2x")]
    [InlineData("700")]        // out of 100-599 range
    [InlineData("250-200")]    // reversed range
    [InlineData("200-")]       // incomplete range
    [InlineData(",")]          // F7: all-empty tokens → "empty status spec"
    public void Invalid_specs_report_error(string spec)
    {
        Assert.False(StatusSpec.TryParse(spec, out _, out string? error));
        Assert.NotNull(error);
    }
}
