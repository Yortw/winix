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
}
