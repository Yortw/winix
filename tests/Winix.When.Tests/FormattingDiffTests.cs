// tests/Winix.When.Tests/FormattingDiffTests.cs
using Xunit;
using Winix.When;

namespace Winix.When.Tests;

public class FormattingDiffDefaultTests
{
    private static readonly DateTimeOffset From = new(2024, 6, 18, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2024, 6, 25, 4, 12, 0, TimeSpan.Zero);

    [Fact]
    public void FormatDiff_ContainsDurationLine()
    {
        TimeSpan duration = To - From;
        string output = Formatting.FormatDiff(duration, From, To, displayTz: null, useColor: false);
        Assert.Contains("Duration:", output);
        Assert.Contains("7 days", output);
    }

    [Fact]
    public void FormatDiff_ContainsIsoLine()
    {
        TimeSpan duration = To - From;
        string output = Formatting.FormatDiff(duration, From, To, displayTz: null, useColor: false);
        Assert.Contains("ISO 8601:", output);
        Assert.Contains("P7DT4H12M", output);
    }

    [Fact]
    public void FormatDiff_ContainsSecondsLine()
    {
        TimeSpan duration = To - From;
        string output = Formatting.FormatDiff(duration, From, To, displayTz: null, useColor: false);
        Assert.Contains("Seconds:", output);
    }

    [Fact]
    public void FormatDiff_NegativeDuration_ReordersFromTo()
    {
        // From is later than To — human output should reorder
        TimeSpan duration = From - To; // negative
        string output = Formatting.FormatDiff(duration, To, From, displayTz: null, useColor: false);
        // Duration should be positive (absolute value) in human output
        Assert.Contains("7 days", output);
        Assert.DoesNotContain("-P", output);
    }

    [Fact]
    public void FormatDiff_WithTz_ContainsFromToLines()
    {
        var tokyoTz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        TimeSpan duration = To - From;
        string output = Formatting.FormatDiff(duration, From, To, displayTz: tokyoTz, useColor: false);
        Assert.Contains("From:", output);
        Assert.Contains("To:", output);
        Assert.Contains("+09:00", output);
    }

    [Fact]
    public void FormatDiff_NoTz_NoFromToLines()
    {
        TimeSpan duration = To - From;
        string output = Formatting.FormatDiff(duration, From, To, displayTz: null, useColor: false);
        Assert.DoesNotContain("From:", output);
        Assert.DoesNotContain("To:", output);
    }

    [Fact]
    public void FormatDiff_WithColor_ContainsAnsi()
    {
        TimeSpan duration = To - From;
        string output = Formatting.FormatDiff(duration, From, To, displayTz: null, useColor: true);
        Assert.Contains("\x1b[", output);
    }
}

public class FormattingDiffIsoTests
{
    [Fact]
    public void FormatDiffIso_Positive()
    {
        var duration = new TimeSpan(7, 4, 12, 0);
        string output = Formatting.FormatDiffIso(duration);
        Assert.Equal("P7DT4H12M", output);
    }

    [Fact]
    public void FormatDiffIso_Negative()
    {
        var duration = new TimeSpan(-7, -4, -12, 0);
        string output = Formatting.FormatDiffIso(duration);
        Assert.StartsWith("-P", output);
    }

    [Fact]
    public void FormatDiffIso_Zero()
    {
        string output = Formatting.FormatDiffIso(TimeSpan.Zero);
        Assert.Equal("PT0S", output);
    }
}
