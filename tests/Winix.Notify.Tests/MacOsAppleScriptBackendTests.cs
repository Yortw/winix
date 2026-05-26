#nullable enable
using System;
using System.Threading;
using Xunit;
using Winix.Notify;

namespace Winix.Notify.Tests;

public class MacOsAppleScriptBackendTests
{
    [Fact]
    public void Backend_Name_IsMacosOsascript()
    {
        var b = new MacOsAppleScriptBackend();
        Assert.Equal("macos-osascript", b.Name);
    }

    // -- Round-1 review TA-I3 — pin the failure-path naming on non-macOS the same way
    //    we did for Linux: a deterministic "binary not found" error rather than relying
    //    on opportunistic CI behaviour. --
    [SkippableFact]
    public async System.Threading.Tasks.Task Send_OnNonMacOs_FailsWithOsascriptNotFound()
    {
        Skip.If(OperatingSystem.IsMacOS(), "Non-macOS platforms — pins the osascript-not-found failure-path message.");

        var b = new MacOsAppleScriptBackend();
        var result = await b.SendAsync(new NotifyMessage("t", "b", Urgency.Normal, null), CancellationToken.None);
        Assert.Equal("macos-osascript", result.BackendName);
        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        Assert.Contains("osascript", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("hello", "hello")]
    [InlineData("with \"quotes\"", "with \\\"quotes\\\"")]
    [InlineData("with \\backslash", "with \\\\backslash")]
    public void EscapeForApplescript_ProducesCorrectQuoting(string input, string expected)
    {
        // Backslash MUST be doubled before the quote-escape, otherwise " -> \" introduces
        // an unescaped backslash that AppleScript would interpret as a string-escape escape.
        Assert.Equal(expected, MacOsAppleScriptBackend.EscapeForApplescript(input));
    }

    [Fact]
    public void BuildScript_TitleAndBody_AssemblesDisplayNotificationCommand()
    {
        string script = MacOsAppleScriptBackend.BuildScript(
            new NotifyMessage("the title", "the body", Urgency.Normal, null));
        Assert.Equal("display notification \"the body\" with title \"the title\"", script);
    }

    [Fact]
    public void BuildScript_NullBody_RendersEmptyBodyString()
    {
        string script = MacOsAppleScriptBackend.BuildScript(
            new NotifyMessage("the title", null, Urgency.Normal, null));
        Assert.Equal("display notification \"\" with title \"the title\"", script);
    }

    [Fact]
    public void BuildScript_Critical_AppendsSubmarineSound()
    {
        string script = MacOsAppleScriptBackend.BuildScript(
            new NotifyMessage("alert", null, Urgency.Critical, null));
        Assert.EndsWith(" sound name \"Submarine\"", script);
    }

    [Fact]
    public void BuildScript_LowAndNormal_NoSound()
    {
        Assert.DoesNotContain("sound name", MacOsAppleScriptBackend.BuildScript(
            new NotifyMessage("a", null, Urgency.Low, null)));
        Assert.DoesNotContain("sound name", MacOsAppleScriptBackend.BuildScript(
            new NotifyMessage("a", null, Urgency.Normal, null)));
    }
}
