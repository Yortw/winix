#nullable enable
using System.Runtime.Versioning;
using Xunit;
using Winix.Notify;

namespace Winix.Notify.Tests;

[SupportedOSPlatform("windows")]
public class WindowsToastBackendTests
{
    [Fact]
    public void BuildToastXml_TitleOnly_HasOneTextLine()
    {
        string xml = WindowsToastBackend.BuildToastXml(new NotifyMessage("hello", null, Urgency.Normal, null));
        Assert.Contains("<text>hello</text>", xml);
        Assert.DoesNotContain("scenario=", xml);
        Assert.DoesNotContain("<audio", xml);
    }

    [Fact]
    public void BuildToastXml_TitleAndBody_HasTwoTextLines()
    {
        string xml = WindowsToastBackend.BuildToastXml(new NotifyMessage("hi", "world", Urgency.Normal, null));
        Assert.Contains("<text>hi</text>", xml);
        Assert.Contains("<text>world</text>", xml);
    }

    [Fact]
    public void BuildToastXml_Critical_HasUrgentScenario()
    {
        string xml = WindowsToastBackend.BuildToastXml(new NotifyMessage("hi", null, Urgency.Critical, null));
        Assert.Contains("scenario=\"urgent\"", xml);
    }

    [Fact]
    public void BuildToastXml_Low_HasSilentAudio()
    {
        string xml = WindowsToastBackend.BuildToastXml(new NotifyMessage("hi", null, Urgency.Low, null));
        Assert.Contains("<audio silent=\"true\"/>", xml);
    }

    [Fact]
    public void BuildToastXml_EscapesXmlSpecialCharacters()
    {
        string xml = WindowsToastBackend.BuildToastXml(new NotifyMessage("a&b<c>d", null, Urgency.Normal, null));
        Assert.Contains("a&amp;b&lt;c&gt;d", xml);
    }

    [Fact]
    public void BuildToastXml_IconPath_AppearsAsAppLogoOverride()
    {
        string xml = WindowsToastBackend.BuildToastXml(new NotifyMessage("hi", null, Urgency.Normal, "C:\\icon.png"));
        Assert.Contains("placement=\"appLogoOverride\"", xml);
        Assert.Contains("src=\"C:\\icon.png\"", xml);
    }

    [Fact]
    public void BuildPowerShellScript_EscapesSingleQuotesInXmlAndAumid()
    {
        // Single-quoted PS strings escape inner ' as ''. We never emit ' in our XML
        // (EscapeXml turns " into &quot; and there's no source of ' in our composer)
        // but the AUMID value is user-defined-ish, so verify both inputs are escaped.
        string xml = "<toast><visual>'</visual></toast>";
        string aumid = "with'quote";
        string script = WindowsToastBackend.BuildPowerShellScript(aumid, xml);
        Assert.Contains("'<toast><visual>''</visual></toast>'", script);
        Assert.Contains("'with''quote'", script);
    }

    [Fact]
    public void BuildPowerShellScript_LoadsWinRtTypes()
    {
        string script = WindowsToastBackend.BuildPowerShellScript("aumid", "<toast/>");
        Assert.Contains("ToastNotificationManager", script);
        Assert.Contains("XmlDocument", script);
        Assert.Contains("ContentType=WindowsRuntime", script);
    }

    // -- Round-2 review test gap (SFH-C1 contract pin) — the round-1 fix converts
    //    AumidShortcut registration failures from silent toast drops (Ok=true, no
    //    notification) into typed BackendResult failures. The contract was unguarded
    //    until this test. The internal seam lets us inject a synthetic shortcut failure
    //    without needing a locked-down Windows profile / AV-blocked lnk write to
    //    reproduce. The PowerShell call site must NEVER run when the shortcut fails. --
    [Fact]
    public async System.Threading.Tasks.Task SendAsync_ShortcutRegistrationFails_ReturnsTypedFailure_DoesNotInvokePowerShell()
    {
        try
        {
            // Inject a synthetic shortcut failure with a distinctive marker we can assert on.
            WindowsToastBackend.AumidShortcutOverride = (out string? error) =>
            {
                error = "synthetic AV block (test marker)";
                return false;
            };

            var b = new WindowsToastBackend();
            var result = await b.SendAsync(new NotifyMessage("hi", "world", Urgency.Normal, null), System.Threading.CancellationToken.None);

            Assert.Equal("windows-toast", result.BackendName);
            Assert.False(result.Ok); // SFH-C1 contract: silent drop must NOT report Ok=true
            Assert.NotNull(result.Error);
            Assert.Contains("AUMID shortcut", result.Error, System.StringComparison.Ordinal);
            Assert.Contains("synthetic AV block (test marker)", result.Error, System.StringComparison.Ordinal);
        }
        finally
        {
            WindowsToastBackend.AumidShortcutOverride = null;
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task SendAsync_ShortcutNullErrorWithFalseReturn_FallsBackToUnknownReason()
    {
        // Defensive: pin the `?? "unknown reason"` fallback. Even though the production
        // EnsureExists never returns (false, null), a future refactor could regress that;
        // the fallback ensures the user always gets some context.
        try
        {
            WindowsToastBackend.AumidShortcutOverride = (out string? error) =>
            {
                error = null;
                return false;
            };

            var b = new WindowsToastBackend();
            var result = await b.SendAsync(new NotifyMessage("hi", null, Urgency.Normal, null), System.Threading.CancellationToken.None);

            Assert.False(result.Ok);
            Assert.NotNull(result.Error);
            Assert.Contains("unknown reason", result.Error, System.StringComparison.Ordinal);
        }
        finally
        {
            WindowsToastBackend.AumidShortcutOverride = null;
        }
    }
}
