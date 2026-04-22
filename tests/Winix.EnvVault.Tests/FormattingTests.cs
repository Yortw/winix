#nullable enable
using Winix.EnvVault;
using Xunit;

namespace Winix.EnvVault.Tests;

public class FormattingTests
{
    [Fact]
    public void FormatNamespaceList_Plain_OneNamespacePerLine()
    {
        string s = Formatting.FormatNamespaceList(new[] { "github", "aws" }, json: false);
        Assert.Equal("github\naws\n", s);
    }

    [Fact]
    public void FormatNamespaceList_Json_EmitsJsonArray()
    {
        string s = Formatting.FormatNamespaceList(new[] { "github", "aws" }, json: true);
        Assert.Equal("[\"github\",\"aws\"]", s);
    }

    [Fact]
    public void FormatKeyList_Plain_OneKeyPerLine()
    {
        string s = Formatting.FormatKeyList(new[] { "TOKEN", "USER" }, json: false);
        Assert.Equal("TOKEN\nUSER\n", s);
    }

    [Fact]
    public void RequirePassphraseError_MentionsV11AndNativeBackend()
    {
        string s = Formatting.RequirePassphraseDeferredError();
        Assert.Contains("v1.1", s);
        Assert.Contains("native", s, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValueOnArgvWarning_MentionsArgvAndHistory()
    {
        string s = Formatting.ValueOnArgvWarning();
        Assert.Contains("argv", s, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("history", s, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatKeyList_Json_EmitsJsonArray()
    {
        string s = Formatting.FormatKeyList(new[] { "TOKEN", "USER" }, json: true);
        Assert.Equal("[\"TOKEN\",\"USER\"]", s);
    }

    [Fact]
    public void FormatNamespaceList_JsonEmpty_EmitsEmptyArray()
    {
        string s = Formatting.FormatNamespaceList(System.Array.Empty<string>(), json: true);
        Assert.Equal("[]", s);
    }

    [Fact]
    public void FormatKeyList_JsonWithEmbeddedQuotesAndBackslashes_Escaped()
    {
        string s = Formatting.FormatKeyList(new[] { "a\"b", "c\\d" }, json: true);
        Assert.Equal("[\"a\\\"b\",\"c\\\\d\"]", s);
    }

    [Fact]
    public void GetToTtyWarning_MentionsScrollbackAndExecAlternative()
    {
        // Regression: ensure the warning text still advises the exec-form alternative. Future
        // message rewrites shouldn't silently drop the advice that makes the warning actionable.
        string s = Formatting.GetToTtyWarning();
        Assert.Contains("scrollback", s, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("envvault", s, System.StringComparison.OrdinalIgnoreCase);
    }
}
