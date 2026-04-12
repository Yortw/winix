#nullable enable

using System;
using System.Collections.Generic;
using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

public sealed class CronFieldTests
{
    // --- Wildcard ---

    [Fact]
    public void Parse_Wildcard_ReturnsAllValues()
    {
        var field = CronField.Parse("*", 0, 59);

        Assert.Equal(60, field.Values.Count);
        Assert.Contains(0, field.Values);
        Assert.Contains(59, field.Values);
    }

    // --- Single value ---

    [Fact]
    public void Parse_SingleValue_ReturnsOneValue()
    {
        var field = CronField.Parse("5", 0, 59);

        Assert.Single(field.Values);
        Assert.Contains(5, field.Values);
    }

    [Fact]
    public void Parse_SingleValue_BelowMin_Throws()
    {
        Assert.Throws<FormatException>(() => CronField.Parse("-1", 0, 59));
    }

    [Fact]
    public void Parse_SingleValue_AboveMax_Throws()
    {
        Assert.Throws<FormatException>(() => CronField.Parse("60", 0, 59));
    }

    // --- Ranges ---

    [Fact]
    public void Parse_Range_ReturnsInclusive()
    {
        var field = CronField.Parse("1-5", 0, 59);

        Assert.Equal(5, field.Values.Count);
        Assert.Contains(1, field.Values);
        Assert.Contains(5, field.Values);
        Assert.DoesNotContain(0, field.Values);
        Assert.DoesNotContain(6, field.Values);
    }

    [Fact]
    public void Parse_Range_StartAboveEnd_Throws()
    {
        Assert.Throws<FormatException>(() => CronField.Parse("10-5", 0, 59));
    }

    [Fact]
    public void Parse_Range_BelowMin_Throws()
    {
        Assert.Throws<FormatException>(() => CronField.Parse("0-5", 1, 31));
    }

    [Fact]
    public void Parse_Range_AboveMax_Throws()
    {
        Assert.Throws<FormatException>(() => CronField.Parse("1-32", 1, 31));
    }

    // --- Steps ---

    [Fact]
    public void Parse_WildcardStep_ReturnsEveryN()
    {
        var field = CronField.Parse("*/5", 0, 59);

        // 0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55
        Assert.Equal(12, field.Values.Count);
        Assert.Contains(0, field.Values);
        Assert.Contains(5, field.Values);
        Assert.Contains(55, field.Values);
        Assert.DoesNotContain(1, field.Values);
    }

    [Fact]
    public void Parse_RangeStep_ReturnsEveryNInRange()
    {
        var field = CronField.Parse("1-10/3", 0, 59);

        // 1, 4, 7, 10
        Assert.Equal(4, field.Values.Count);
        Assert.Contains(1, field.Values);
        Assert.Contains(4, field.Values);
        Assert.Contains(7, field.Values);
        Assert.Contains(10, field.Values);
    }

    [Fact]
    public void Parse_Step_Zero_Throws()
    {
        Assert.Throws<FormatException>(() => CronField.Parse("*/0", 0, 59));
    }

    [Fact]
    public void Parse_SingleValueStep_ReturnsEveryNFromValue()
    {
        // "5/10" means starting at 5, every 10: 5, 15, 25, 35, 45, 55
        var field = CronField.Parse("5/10", 0, 59);

        Assert.Equal(6, field.Values.Count);
        Assert.Contains(5, field.Values);
        Assert.Contains(15, field.Values);
        Assert.Contains(55, field.Values);
    }

    // --- Lists ---

    [Fact]
    public void Parse_List_ReturnsAllSpecifiedValues()
    {
        var field = CronField.Parse("1,3,5", 0, 59);

        Assert.Equal(3, field.Values.Count);
        Assert.Contains(1, field.Values);
        Assert.Contains(3, field.Values);
        Assert.Contains(5, field.Values);
    }

    [Fact]
    public void Parse_List_WithRanges()
    {
        var field = CronField.Parse("1-3,7,10-12", 0, 59);

        Assert.Equal(7, field.Values.Count);
        Assert.Contains(1, field.Values);
        Assert.Contains(2, field.Values);
        Assert.Contains(3, field.Values);
        Assert.Contains(7, field.Values);
        Assert.Contains(10, field.Values);
        Assert.Contains(11, field.Values);
        Assert.Contains(12, field.Values);
    }

    [Fact]
    public void Parse_List_WithSteps()
    {
        var field = CronField.Parse("0-10/5,30", 0, 59);

        // 0, 5, 10, 30
        Assert.Equal(4, field.Values.Count);
        Assert.Contains(0, field.Values);
        Assert.Contains(5, field.Values);
        Assert.Contains(10, field.Values);
        Assert.Contains(30, field.Values);
    }

    [Fact]
    public void Parse_List_DuplicatesDeduped()
    {
        var field = CronField.Parse("1,1,2,2", 0, 59);

        Assert.Equal(2, field.Values.Count);
    }

    // --- Named values ---

    [Fact]
    public void Parse_MonthNames()
    {
        var field = CronField.Parse("jan-mar", 1, 12, CronField.MonthNames);

        Assert.Equal(3, field.Values.Count);
        Assert.Contains(1, field.Values);
        Assert.Contains(2, field.Values);
        Assert.Contains(3, field.Values);
    }

    [Fact]
    public void Parse_DayNames()
    {
        var field = CronField.Parse("mon-fri", 0, 7, CronField.DayOfWeekNames);

        Assert.Equal(5, field.Values.Count);
        Assert.Contains(1, field.Values);
        Assert.Contains(2, field.Values);
        Assert.Contains(3, field.Values);
        Assert.Contains(4, field.Values);
        Assert.Contains(5, field.Values);
    }

    [Fact]
    public void Parse_DayNames_CaseInsensitive()
    {
        var field = CronField.Parse("MON", 0, 7, CronField.DayOfWeekNames);

        Assert.Single(field.Values);
        Assert.Contains(1, field.Values);
    }

    [Fact]
    public void Parse_Sunday_Zero_And_Seven_BothMap()
    {
        // Both 0 and 7 should map to Sunday (0)
        var field0 = CronField.Parse("0", 0, 7, CronField.DayOfWeekNames);
        var field7 = CronField.Parse("7", 0, 7, CronField.DayOfWeekNames);

        Assert.Contains(0, field0.Values);
        Assert.Contains(0, field7.Values);
    }

    // --- Error cases ---

    [Fact]
    public void Parse_Empty_Throws()
    {
        Assert.Throws<FormatException>(() => CronField.Parse("", 0, 59));
    }

    [Fact]
    public void Parse_InvalidToken_Throws()
    {
        Assert.Throws<FormatException>(() => CronField.Parse("abc", 0, 59));
    }

    [Fact]
    public void Parse_TrailingComma_Throws()
    {
        Assert.Throws<FormatException>(() => CronField.Parse("1,", 0, 59));
    }

    // --- Contains ---

    [Fact]
    public void Contains_MatchingValue_ReturnsTrue()
    {
        var field = CronField.Parse("5,10,15", 0, 59);

        Assert.True(field.Contains(10));
    }

    [Fact]
    public void Contains_NonMatchingValue_ReturnsFalse()
    {
        var field = CronField.Parse("5,10,15", 0, 59);

        Assert.False(field.Contains(7));
    }
}
