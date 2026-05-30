using System.Collections.Generic;
using Winix.HCat;
using Xunit;

namespace Winix.HCat.Tests;

public class RequestRecordTests
{
    [Fact]
    public void ToJsonl_emits_single_line_with_expected_keys()
    {
        var r = new RequestRecord(
            Method: "POST",
            Path: "/hook",
            Query: "a=1",
            Headers: new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            Body: "{\"x\":1}",
            Timestamp: "2026-05-30T04:00:00Z",
            RemoteAddr: "127.0.0.1");

        string line = RequestRecord.ToJsonl(r);

        Assert.DoesNotContain('\n', line);
        Assert.Contains("\"method\":\"POST\"", line);
        Assert.Contains("\"path\":\"/hook\"", line);
        Assert.Contains("\"query\":\"a=1\"", line);
        Assert.Contains("\"remote\":\"127.0.0.1\"", line);
        Assert.Contains("\"Content-Type\":\"application/json\"", line);
    }

    [Fact]
    public void ToJsonl_escapes_control_characters_in_body()
    {
        var r = new RequestRecord("GET", "/", "", new Dictionary<string, string>(), "line1\nline2", "t", "::1");
        string line = RequestRecord.ToJsonl(r);
        Assert.DoesNotContain('\n', line.TrimEnd());   // the embedded newline must be escaped, not literal
        Assert.Contains("line1\\nline2", line);
    }
}
