#nullable enable

using System;
using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

public sealed class CronExpressionParseTests
{
    [Fact]
    public void Parse_AllWildcards_Succeeds()
    {
        var expr = CronExpression.Parse("* * * * *");

        Assert.Equal("* * * * *", expr.Expression);
    }

    [Fact]
    public void Parse_SpecificValues_Succeeds()
    {
        var expr = CronExpression.Parse("0 2 1 6 3");

        Assert.Equal("0 2 1 6 3", expr.Expression);
    }

    [Fact]
    public void Parse_Steps_Succeeds()
    {
        var expr = CronExpression.Parse("*/5 */2 * * *");

        Assert.Equal("*/5 */2 * * *", expr.Expression);
    }

    [Fact]
    public void Parse_Ranges_Succeeds()
    {
        var expr = CronExpression.Parse("0 9-17 * * 1-5");

        Assert.Equal("0 9-17 * * 1-5", expr.Expression);
    }

    [Fact]
    public void Parse_Lists_Succeeds()
    {
        var expr = CronExpression.Parse("0,30 * * * *");

        Assert.Equal("0,30 * * * *", expr.Expression);
    }

    [Fact]
    public void Parse_MonthNames_Succeeds()
    {
        var expr = CronExpression.Parse("0 0 1 jan-mar *");

        Assert.Equal("0 0 1 jan-mar *", expr.Expression);
    }

    [Fact]
    public void Parse_DayNames_Succeeds()
    {
        var expr = CronExpression.Parse("0 9 * * mon-fri");

        Assert.Equal("0 9 * * mon-fri", expr.Expression);
    }

    [Fact]
    public void Parse_ExtraWhitespace_Trimmed()
    {
        // Leading/trailing whitespace on the whole expression should be trimmed;
        // the stored Expression reflects the trimmed form.
        var expr = CronExpression.Parse("  0  2  *  *  *  ");

        Assert.Equal("0  2  *  *  *", expr.Expression);
    }

    [Fact]
    public void Parse_TooFewFields_Throws()
    {
        Assert.Throws<FormatException>(() => CronExpression.Parse("* * * *"));
    }

    [Fact]
    public void Parse_TooManyFields_Throws()
    {
        Assert.Throws<FormatException>(() => CronExpression.Parse("* * * * * *"));
    }

    [Fact]
    public void Parse_Empty_Throws()
    {
        Assert.Throws<FormatException>(() => CronExpression.Parse(""));
    }

    [Fact]
    public void Parse_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CronExpression.Parse(null!));
    }

    [Fact]
    public void Parse_InvalidMinute_Throws()
    {
        Assert.Throws<FormatException>(() => CronExpression.Parse("60 * * * *"));
    }

    [Fact]
    public void Parse_InvalidHour_Throws()
    {
        Assert.Throws<FormatException>(() => CronExpression.Parse("0 24 * * *"));
    }

    [Fact]
    public void Parse_InvalidDayOfMonth_Throws()
    {
        Assert.Throws<FormatException>(() => CronExpression.Parse("0 0 32 * *"));
    }

    [Fact]
    public void Parse_InvalidMonth_Throws()
    {
        Assert.Throws<FormatException>(() => CronExpression.Parse("0 0 * 13 *"));
    }

    [Fact]
    public void Parse_InvalidDayOfWeek_Throws()
    {
        Assert.Throws<FormatException>(() => CronExpression.Parse("0 0 * * 8"));
    }

    [Fact]
    public void Parse_Sunday7_IsValid()
    {
        // 7 is an alias for Sunday (0) in cron — must not throw.
        var expr = CronExpression.Parse("0 0 * * 7");

        Assert.NotNull(expr);
    }

    [Fact]
    public void Parse_PreservesOriginalExpression()
    {
        const string input = "*/15 9-17 * * mon-fri";
        var expr = CronExpression.Parse(input);

        Assert.Equal(input, expr.Expression);
    }
}
