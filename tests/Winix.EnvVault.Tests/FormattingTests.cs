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
    public void FormatNamespaceList_Json_EmitsJsonArrayWithTrailingNewline()
    {
        // Trailing newline so shell prompts don't run into the output (bug caught during manual
        // testing — pre-fix output was '["x"]' with no terminator, so $ prompt appeared on same line).
        string s = Formatting.FormatNamespaceList(new[] { "github", "aws" }, json: true);
        Assert.Equal("[\"github\",\"aws\"]\n", s);
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
    public void FormatKeyList_Json_EmitsJsonArrayWithTrailingNewline()
    {
        string s = Formatting.FormatKeyList(new[] { "TOKEN", "USER" }, json: true);
        Assert.Equal("[\"TOKEN\",\"USER\"]\n", s);
    }

    [Fact]
    public void FormatNamespaceList_JsonEmpty_EmitsEmptyArrayWithTrailingNewline()
    {
        string s = Formatting.FormatNamespaceList(System.Array.Empty<string>(), json: true);
        Assert.Equal("[]\n", s);
    }

    [Fact]
    public void FormatKeyList_JsonWithEmbeddedQuotesAndBackslashes_Escaped()
    {
        string s = Formatting.FormatKeyList(new[] { "a\"b", "c\\d" }, json: true);
        Assert.Equal("[\"a\\\"b\",\"c\\\\d\"]\n", s);
    }

    [Fact]
    public void FormatKeyList_JsonWithControlCharacters_ProducesValidJson()
    {
        // Regression: the previous hand-rolled escaper only handled " and \, leaving raw
        // control chars (\n, \t, \r, \0) between the quotes. That output was invalid JSON and
        // would crash any conforming parser (jq, JsonDocument). Now uses Yort.ShellKit.JsonHelper
        // which wraps Utf8JsonWriter's correct escaping. Parse-round-trip confirms validity.
        string s = Formatting.FormatKeyList(new[] { "line\none", "tab\there", "null\0byte" }, json: true);
        // Must be parseable as JSON (previous implementation would throw JsonException here).
        System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(s.TrimEnd('\n'));
        Assert.Equal(System.Text.Json.JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(3, doc.RootElement.GetArrayLength());
        Assert.Equal("line\none", doc.RootElement[0].GetString());
        Assert.Equal("tab\there", doc.RootElement[1].GetString());
        Assert.Equal("null\0byte", doc.RootElement[2].GetString());
    }

    [Fact]
    public void GetToTtyWarning_MentionsScrollbackAndExecAlternative()
    {
        // Regression: ensure the warning text still advises the exec-form alternative. Future
        // message rewrites shouldn't silently drop the advice that makes the warning actionable.
        string s = Formatting.GetToTtyWarning();
        Assert.Contains("scrollback", s, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("envvault", s, System.StringComparison.OrdinalIgnoreCase);
        // The suggested invocation MUST NOT include a '--' separator — envvault's exec form does
        // not use one (a user copy-pasting the suggestion would hit a syntax error). Caught during
        // manual testing.
        Assert.DoesNotContain(" -- ", s);
    }
}
