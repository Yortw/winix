#nullable enable
using Winix.EnvVault;
using Xunit;

namespace Winix.EnvVault.Tests;

public class FormattingTests
{
    // ANSI CSI prefix — ESC followed by '['. Centralised here so the test assertions express
    // intent clearly and don't depend on anyone recognising a raw 0x1B character in source.
    private const string Csi = "[";
    private const string SgrReset = "[0m";

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
        string s = Formatting.RequirePassphraseDeferredError(useColor: false);
        Assert.Contains("v1.1", s);
        Assert.Contains("native", s, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValueOnArgvWarning_MentionsArgvAndHistory()
    {
        string s = Formatting.ValueOnArgvWarning(useColor: false);
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
    public void ErrorLine_UseColorTrue_WrapsWithRedCsiAndReset()
    {
        // I-R6-1 (round 6): the useColor branch of ErrorLine was plumbed through every call site
        // in round 5, but no test actually exercised the true branch. A regression that dropped
        // the AnsiColor.Reset(true) at the tail would leak red colouring onto the shell prompt
        // after envvault exits — invisible to tests that only run with useColor:false.
        string colored = Formatting.ErrorLine("boom", useColor: true);
        Assert.StartsWith(Csi, colored);                         // ANSI CSI prefix (red SGR)
        Assert.Contains("envvault: boom", colored);              // message content preserved
        Assert.EndsWith(SgrReset, colored);                      // closes the SGR so nothing leaks

        // And confirm plain mode omits all escapes (no regression in the opposite direction).
        string plain = Formatting.ErrorLine("boom", useColor: false);
        Assert.DoesNotContain(Csi, plain);
        Assert.Equal("envvault: boom", plain);
    }

    [Fact]
    public void WarningLine_UseColorTrue_WrapsWithYellowCsiAndReset()
    {
        // Mirror of the Error test for the yellow branch. A colour-emission regression on warnings
        // is less severe than on errors (warnings are informational) but still worth pinning.
        string colored = Formatting.WarningLine("careful", useColor: true);
        Assert.StartsWith(Csi, colored);
        Assert.Contains("envvault: warning: careful", colored);
        Assert.EndsWith(SgrReset, colored);

        string plain = Formatting.WarningLine("careful", useColor: false);
        Assert.DoesNotContain(Csi, plain);
        Assert.Equal("envvault: warning: careful", plain);
    }

    [Fact]
    public void ValueOnArgvWarning_UseColorTrue_EmitsYellowWrappedWarning()
    {
        // Round-trip verify that the canonical warning messages route through WarningLine and
        // therefore honour useColor. A regression that bypassed WarningLine to emit plain text
        // would break NO_COLOR opt-out parity for this specific warning.
        string colored = Formatting.ValueOnArgvWarning(useColor: true);
        Assert.StartsWith(Csi, colored);
        Assert.EndsWith(SgrReset, colored);
        Assert.Contains("warning:", colored);
        Assert.Contains("argv", colored);
    }

    [Fact]
    public void GetToTtyWarning_MentionsScrollbackAndExecAlternative()
    {
        // Regression: ensure the warning text still advises the exec-form alternative. Future
        // message rewrites shouldn't silently drop the advice that makes the warning actionable.
        string s = Formatting.GetToTtyWarning(useColor: false);
        Assert.Contains("scrollback", s, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("envvault", s, System.StringComparison.OrdinalIgnoreCase);
        // The suggested invocation MUST NOT include a '--' separator — envvault's exec form does
        // not use one (a user copy-pasting the suggestion would hit a syntax error). Caught during
        // manual testing.
        Assert.DoesNotContain(" -- ", s);
    }
}
