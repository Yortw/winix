#nullable enable

using System;
using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

/// <summary>
/// R3 regression pins for the crontab newline-injection class (silent-failure-hunter F1, F2).
/// Without these guards, a user-supplied newline in --name, command, or arguments would be
/// concatenated into the crontab as additional entries — silently registering hidden tasks
/// alongside the legitimate one.
/// </summary>
public sealed class CrontabInjectionTests
{
    [Theory]
    [InlineData("foo\nbar")]
    [InlineData("foo\rbar")]
    [InlineData("foo\r\nbar")]
    public void AddEntry_NameWithNewline_Throws(string injectedName)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            CrontabParser.AddEntry("", injectedName, "0 2 * * *", "/bin/legit"));
        Assert.Equal("name", ex.ParamName);
    }

    [Theory]
    [InlineData("0 2 * * *\n0 0 * * *")]
    [InlineData("0 2 * * *\r0 0 * * *")]
    public void AddEntry_CronExpressionWithNewline_Throws(string injectedCron)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            CrontabParser.AddEntry("", "task", injectedCron, "/bin/legit"));
        Assert.Equal("cronExpression", ex.ParamName);
    }

    [Theory]
    [InlineData("/bin/run\n# winix:hidden\n0 0 * * * /malicious")]
    [InlineData("/bin/run\r# winix:hidden\r0 0 * * * /malicious")]
    public void AddEntry_CommandWithNewline_Throws(string injectedCommand)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            CrontabParser.AddEntry("", "task", "0 2 * * *", injectedCommand));
        Assert.Equal("command", ex.ParamName);
    }

    [Fact]
    public void AddEntry_NameContainingWinixTagPrefix_Throws()
    {
        // Even if a name is on a single line, embedding the tag prefix could allow forging
        // an additional tag if combined with other vectors. Reject defensively.
        var ex = Assert.Throws<ArgumentException>(() =>
            CrontabParser.AddEntry("", "real# winix:fake", "0 2 * * *", "/bin/run"));
        Assert.Equal("name", ex.ParamName);
        Assert.Contains("forge", ex.Message);
    }

    [Fact]
    public void AddEntry_CleanInputs_StillSucceeds()
    {
        // Sanity: the validation doesn't break the happy path.
        string result = CrontabParser.AddEntry("", "ok-task", "0 2 * * *", "/bin/run --flag");
        Assert.Contains("# winix:ok-task", result);
        Assert.Contains("0 2 * * * /bin/run --flag", result);
    }

    [Fact]
    public void AddEntry_NameNullThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CrontabParser.AddEntry("", null!, "0 2 * * *", "/bin/run"));
    }
}
