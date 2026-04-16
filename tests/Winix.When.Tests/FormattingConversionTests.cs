// tests/Winix.When.Tests/FormattingConversionTests.cs
using Xunit;
using Winix.When;

namespace Winix.When.Tests;

public class FormattingDefaultTests
{
    private static readonly DateTimeOffset Timestamp = new(2024, 6, 18, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2025, 5, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo NzTz = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Auckland");

    [Fact]
    public void FormatDefault_ContainsUtcLine()
    {
        string output = Formatting.FormatDefault(Timestamp, NzTz, extraTz: null, Now, useColor: false);
        Assert.Contains("UTC:", output);
        Assert.Contains("2024-06-18T20:00:00Z", output);
    }

    [Fact]
    public void FormatDefault_ContainsLocalLine()
    {
        string output = Formatting.FormatDefault(Timestamp, NzTz, extraTz: null, Now, useColor: false);
        Assert.Contains("Local:", output);
        // Auckland in June = NZST (+12:00)
        Assert.Contains("+12:00", output);
    }

    [Fact]
    public void FormatDefault_ContainsRelativeLine()
    {
        string output = Formatting.FormatDefault(Timestamp, NzTz, extraTz: null, Now, useColor: false);
        Assert.Contains("Relative:", output);
        Assert.Contains("ago", output);
    }

    [Fact]
    public void FormatDefault_ContainsUnixLine()
    {
        string output = Formatting.FormatDefault(Timestamp, NzTz, extraTz: null, Now, useColor: false);
        Assert.Contains("Unix:", output);
        // 2024-06-18T20:00:00Z = 1718740800
        Assert.Contains("1718740800", output);
    }

    [Fact]
    public void FormatDefault_WithExtraTz_ContainsExtraLine()
    {
        var tokyoTz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        string output = Formatting.FormatDefault(Timestamp, NzTz, extraTz: tokyoTz, Now, useColor: false);
        Assert.Contains("Tokyo:", output);
        Assert.Contains("+09:00", output);
    }

    [Fact]
    public void FormatDefault_WithColor_ContainsAnsiSequences()
    {
        string output = Formatting.FormatDefault(Timestamp, NzTz, extraTz: null, Now, useColor: true);
        Assert.Contains("\x1b[", output);
    }

    [Fact]
    public void FormatDefault_NoColor_NoAnsiSequences()
    {
        string output = Formatting.FormatDefault(Timestamp, NzTz, extraTz: null, Now, useColor: false);
        Assert.DoesNotContain("\x1b[", output);
    }
}

public class FormattingUtcTests
{
    private static readonly DateTimeOffset Timestamp = new(2024, 6, 18, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FormatUtc_ReturnsIsoUtc()
    {
        string output = Formatting.FormatUtc(Timestamp);
        Assert.Equal("2024-06-18T20:00:00Z", output);
    }

    [Fact]
    public void FormatUtc_WithOffset_ConvertsToUtc()
    {
        var tsWithOffset = new DateTimeOffset(2024, 6, 19, 8, 0, 0, TimeSpan.FromHours(12));
        string output = Formatting.FormatUtc(tsWithOffset);
        Assert.Equal("2024-06-18T20:00:00Z", output);
    }
}

public class FormattingLocalTests
{
    private static readonly DateTimeOffset Timestamp = new(2024, 6, 18, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FormatLocal_WithTimezone_ReturnsConverted()
    {
        var tokyoTz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        string output = Formatting.FormatLocal(Timestamp, tokyoTz);
        Assert.Equal("2024-06-19T05:00:00+09:00", output);
    }

    [Fact]
    public void FormatLocal_Utc_ReturnsZ()
    {
        string output = Formatting.FormatLocal(Timestamp, TimeZoneInfo.Utc);
        Assert.Equal("2024-06-18T20:00:00Z", output);
    }
}
