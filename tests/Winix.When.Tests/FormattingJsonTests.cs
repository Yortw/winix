// tests/Winix.When.Tests/FormattingJsonTests.cs
using Xunit;
using Winix.When;

namespace Winix.When.Tests;

public class FormattingJsonConversionTests
{
    private static readonly DateTimeOffset Timestamp = new(2024, 6, 18, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2025, 5, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo NzTz = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Auckland");

    [Fact]
    public void FormatJson_ContainsToolField()
    {
        string json = Formatting.FormatJson(Timestamp, NzTz, extraTz: null, Now,
            inputStr: "1718740800", offsetStr: null, "when", "0.3.0");
        Assert.Contains("\"tool\":\"when\"", json);
    }

    [Fact]
    public void FormatJson_ContainsVersionField()
    {
        string json = Formatting.FormatJson(Timestamp, NzTz, extraTz: null, Now,
            inputStr: "1718740800", offsetStr: null, "when", "0.3.0");
        Assert.Contains("\"version\":\"0.3.0\"", json);
    }

    [Fact]
    public void FormatJson_ContainsExitCode()
    {
        string json = Formatting.FormatJson(Timestamp, NzTz, extraTz: null, Now,
            inputStr: "1718740800", offsetStr: null, "when", "0.3.0");
        Assert.Contains("\"exit_code\":0", json);
    }

    [Fact]
    public void FormatJson_ContainsUtc()
    {
        string json = Formatting.FormatJson(Timestamp, NzTz, extraTz: null, Now,
            inputStr: "1718740800", offsetStr: null, "when", "0.3.0");
        Assert.Contains("\"utc\":\"2024-06-18T20:00:00Z\"", json);
    }

    [Fact]
    public void FormatJson_ContainsUnixSeconds()
    {
        string json = Formatting.FormatJson(Timestamp, NzTz, extraTz: null, Now,
            inputStr: "1718740800", offsetStr: null, "when", "0.3.0");
        // 2024-06-18T20:00:00Z = 1718740800
        Assert.Contains("\"unix_seconds\":1718740800", json);
    }

    [Fact]
    public void FormatJson_ContainsUnixMilliseconds()
    {
        string json = Formatting.FormatJson(Timestamp, NzTz, extraTz: null, Now,
            inputStr: "1718740800", offsetStr: null, "when", "0.3.0");
        // 2024-06-18T20:00:00Z = 1718740800000 ms
        Assert.Contains("\"unix_milliseconds\":1718740800000", json);
    }

    [Fact]
    public void FormatJson_ContainsInput()
    {
        string json = Formatting.FormatJson(Timestamp, NzTz, extraTz: null, Now,
            inputStr: "1718740800", offsetStr: null, "when", "0.3.0");
        Assert.Contains("\"input\":\"1718740800\"", json);
    }

    [Fact]
    public void FormatJson_NullOffset()
    {
        string json = Formatting.FormatJson(Timestamp, NzTz, extraTz: null, Now,
            inputStr: "1718740800", offsetStr: null, "when", "0.3.0");
        Assert.Contains("\"offset\":null", json);
    }

    [Fact]
    public void FormatJson_WithOffset()
    {
        string json = Formatting.FormatJson(Timestamp, NzTz, extraTz: null, Now,
            inputStr: "now", offsetStr: "+7d", "when", "0.3.0");
        Assert.Contains("\"offset\":\"+7d\"", json);
    }

    [Fact]
    public void FormatJson_WithExtraTz_ContainsTargetFields()
    {
        var tokyoTz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        string json = Formatting.FormatJson(Timestamp, NzTz, extraTz: tokyoTz, Now,
            inputStr: "1718740800", offsetStr: null, "when", "0.3.0");
        Assert.Contains("\"target_timezone\":\"JST\"", json);
        Assert.Contains("\"target\":", json);
    }

    [Fact]
    public void FormatJson_NoExtraTz_NoTargetFields()
    {
        string json = Formatting.FormatJson(Timestamp, NzTz, extraTz: null, Now,
            inputStr: "1718740800", offsetStr: null, "when", "0.3.0");
        Assert.DoesNotContain("target_timezone", json);
        Assert.DoesNotContain("\"target\":", json);
    }
}

public class FormattingJsonDiffTests
{
    private static readonly DateTimeOffset From = new(2024, 6, 18, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2024, 6, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FormatDiffJson_ContainsToolField()
    {
        var duration = To - From;
        string json = Formatting.FormatDiffJson(duration, From, To, "when", "0.3.0");
        Assert.Contains("\"tool\":\"when\"", json);
    }

    [Fact]
    public void FormatDiffJson_ContainsFrom()
    {
        var duration = To - From;
        string json = Formatting.FormatDiffJson(duration, From, To, "when", "0.3.0");
        Assert.Contains("\"from\":\"2024-06-18T00:00:00Z\"", json);
    }

    [Fact]
    public void FormatDiffJson_ContainsTo()
    {
        var duration = To - From;
        string json = Formatting.FormatDiffJson(duration, From, To, "when", "0.3.0");
        Assert.Contains("\"to\":\"2024-06-25T00:00:00Z\"", json);
    }

    [Fact]
    public void FormatDiffJson_ContainsDurationIso()
    {
        var duration = To - From;
        string json = Formatting.FormatDiffJson(duration, From, To, "when", "0.3.0");
        Assert.Contains("\"duration_iso\":\"P7DT0H0M\"", json);
    }

    [Fact]
    public void FormatDiffJson_ContainsTotalSeconds()
    {
        var duration = To - From;
        string json = Formatting.FormatDiffJson(duration, From, To, "when", "0.3.0");
        Assert.Contains("\"total_seconds\":604800", json);
    }

    [Fact]
    public void FormatDiffJson_ContainsDaysHoursMinutesSeconds()
    {
        var duration = To - From;
        string json = Formatting.FormatDiffJson(duration, From, To, "when", "0.3.0");
        Assert.Contains("\"days\":7", json);
        Assert.Contains("\"hours\":0", json);
        Assert.Contains("\"minutes\":0", json);
        Assert.Contains("\"seconds\":0", json);
    }

    [Fact]
    public void FormatDiffJson_NegativeDuration_SignedValues()
    {
        // From is later than To
        var duration = From - To; // negative
        string json = Formatting.FormatDiffJson(duration, From, To, "when", "0.3.0");
        Assert.Contains("\"-P7DT0H0M\"", json);
        Assert.Contains("\"total_seconds\":-604800", json);
        Assert.Contains("\"days\":-7", json);
    }

    [Fact]
    public void FormatDiffJson_NegativeDuration_AllComponentsSigned()
    {
        // Duration with non-zero hours/minutes/seconds to verify all get the sign
        var from = new DateTimeOffset(2024, 6, 18, 3, 15, 30, TimeSpan.Zero);
        var to = new DateTimeOffset(2024, 6, 18, 0, 0, 0, TimeSpan.Zero);
        var duration = to - from; // -3h15m30s
        string json = Formatting.FormatDiffJson(duration, from, to, "when", "0.3.0");
        Assert.Contains("\"hours\":-3", json);
        Assert.Contains("\"minutes\":-15", json);
        Assert.Contains("\"seconds\":-30", json);
    }

    [Fact]
    public void FormatDiffJson_PreservesArgumentOrder()
    {
        // Even when From > To, JSON preserves argument order
        var duration = From - To;
        string json = Formatting.FormatDiffJson(duration, From, To, "when", "0.3.0");
        // "from" should be the first argument (2024-06-18), not reordered
        Assert.Contains("\"from\":\"2024-06-18T00:00:00Z\"", json);
        Assert.Contains("\"to\":\"2024-06-25T00:00:00Z\"", json);
    }
}

public class FormattingJsonErrorTests
{
    [Fact]
    public void FormatJsonError_ContainsAllFields()
    {
        string json = Formatting.FormatJsonError(125, "parse_error",
            "Cannot parse 'foo'", "when", "0.3.0");
        Assert.Contains("\"tool\":\"when\"", json);
        Assert.Contains("\"version\":\"0.3.0\"", json);
        Assert.Contains("\"exit_code\":125", json);
        Assert.Contains("\"exit_reason\":\"parse_error\"", json);
        Assert.Contains("\"message\":\"Cannot parse 'foo'\"", json);
    }
}
