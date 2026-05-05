#nullable enable
using Xunit;
using Winix.Qr;
using Winix.QrCode;

namespace Winix.Qr.Tests;

public class ArgParserTests
{
    // Text mode — positional
    [Fact]
    public void Parse_TextPositional_Ok()
    {
        ArgParser.Result r = ArgParser.Parse(["hello"]);
        Assert.Null(r.Error);
        Assert.Equal(SubCommand.Text, r.Options!.SubCommand);
        Assert.Equal("hello", r.Options.TextPayload);
    }

    [Fact]
    public void Parse_TextStdinDash_LeavesTextPayloadNull()
    {
        ArgParser.Result r = ArgParser.Parse(["-"]);
        Assert.Null(r.Error);
        Assert.Equal(SubCommand.Text, r.Options!.SubCommand);
        Assert.Null(r.Options.TextPayload);
        Assert.True(r.ReadStdin);
    }

    [Fact]
    public void Parse_NoPositional_SignalsStdinReadIntent()
    {
        ArgParser.Result r = ArgParser.Parse([]);
        Assert.Null(r.Error);
        Assert.Null(r.Options!.TextPayload);
        Assert.True(r.ReadStdin);
    }

    // Render flags
    [Fact]
    public void Parse_FormatFlag()
    {
        ArgParser.Result r = ArgParser.Parse(["hello", "--format", "svg"]);
        Assert.Equal(OutputFormat.Svg, r.Options!.Format);
    }

    [Theory]
    [InlineData("unicode", OutputFormat.Unicode)]
    [InlineData("ascii",   OutputFormat.Ascii)]
    [InlineData("svg",     OutputFormat.Svg)]
    [InlineData("png",     OutputFormat.Png)]
    [InlineData("auto",    OutputFormat.Auto)]
    public void Parse_FormatAllVariants(string input, OutputFormat expected)
    {
        ArgParser.Result r = ArgParser.Parse(["hello", "--format", input]);
        Assert.Equal(expected, r.Options!.Format);
    }

    [Fact]
    public void Parse_UnknownFormat_Error()
    {
        ArgParser.Result r = ArgParser.Parse(["hello", "--format", "xyz"]);
        Assert.NotNull(r.Error);
        Assert.Contains("format", r.Error!);
    }

    [Fact]
    public void Parse_EccShort_m()
    {
        ArgParser.Result r = ArgParser.Parse(["hello", "-e", "h"]);
        Assert.Equal(EccLevel.H, r.Options!.Ecc);
    }

    [Theory]
    [InlineData("l", EccLevel.L)]
    [InlineData("m", EccLevel.M)]
    [InlineData("q", EccLevel.Q)]
    [InlineData("h", EccLevel.H)]
    public void Parse_EccAllVariants(string input, EccLevel expected)
    {
        ArgParser.Result r = ArgParser.Parse(["hello", "--error-correction", input]);
        Assert.Equal(expected, r.Options!.Ecc);
    }

    [Fact]
    public void Parse_SizeFlag()
    {
        ArgParser.Result r = ArgParser.Parse(["hello", "--size", "20"]);
        Assert.Equal(20, r.Options!.PixelsPerModule);
    }

    [Fact]
    public void Parse_SizeZero_Error()
    {
        ArgParser.Result r = ArgParser.Parse(["hello", "--size", "0"]);
        Assert.NotNull(r.Error);
        Assert.Contains("--size", r.Error!);
    }

    [Fact]
    public void Parse_SizeNegative_Error()
    {
        ArgParser.Result r = ArgParser.Parse(["hello", "--size", "-5"]);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void Parse_NoMarginFlag()
    {
        ArgParser.Result r = ArgParser.Parse(["hello", "--no-margin"]);
        Assert.True(r.Options!.NoMargin);
    }

    [Fact]
    public void Parse_OutputFlag()
    {
        ArgParser.Result r = ArgParser.Parse(["hello", "--output", "code.svg"]);
        Assert.Equal("code.svg", r.Options!.OutputPath);
    }

    [Fact]
    public void Parse_ForceBinaryFlag()
    {
        ArgParser.Result r = ArgParser.Parse(["hello", "--force-binary"]);
        Assert.True(r.Options!.ForceBinary);
    }

    // wifi subcommand
    [Fact]
    public void Parse_Wifi_AllFields()
    {
        ArgParser.Result r = ArgParser.Parse([
            "wifi",
            "--ssid", "HomeNet",
            "--password", "s3cr3t",
            "--security", "wpa2",
            "--hidden",
        ]);
        Assert.Null(r.Error);
        Assert.Equal(SubCommand.Wifi, r.Options!.SubCommand);
        Assert.Equal("HomeNet", r.Options.WifiSsid);
        Assert.Equal("s3cr3t", r.Options.WifiPassword);
        Assert.Equal("wpa2", r.Options.WifiSecurity);
        Assert.True(r.Options.WifiHidden);
    }

    [Fact]
    public void Parse_Wifi_MissingSsid_Error()
    {
        ArgParser.Result r = ArgParser.Parse(["wifi", "--password", "pw", "--security", "wpa2"]);
        Assert.NotNull(r.Error);
        Assert.Contains("--ssid", r.Error!);
    }

    // sms subcommand
    [Fact]
    public void Parse_Sms_AllFields()
    {
        ArgParser.Result r = ArgParser.Parse(["sms", "--number", "+15551234", "--message", "Hi"]);
        Assert.Equal(SubCommand.Sms, r.Options!.SubCommand);
        Assert.Equal("+15551234", r.Options.SmsNumber);
        Assert.Equal("Hi", r.Options.SmsMessage);
    }

    [Fact]
    public void Parse_Sms_MissingNumber_Error()
    {
        ArgParser.Result r = ArgParser.Parse(["sms", "--message", "Hi"]);
        Assert.NotNull(r.Error);
        Assert.Contains("--number", r.Error!);
    }

    // mailto subcommand
    [Fact]
    public void Parse_Mailto_AllFields()
    {
        ArgParser.Result r = ArgParser.Parse([
            "mailto",
            "--to", "a@b.com",
            "--subject", "Hi",
            "--body", "hello",
            "--cc", "c@d.com",
            "--bcc", "e@f.com",
        ]);
        Assert.Equal(SubCommand.Mailto, r.Options!.SubCommand);
        Assert.Equal("a@b.com", r.Options.MailtoTo);
        Assert.Equal("Hi", r.Options.MailtoSubject);
        Assert.Equal("hello", r.Options.MailtoBody);
        Assert.Equal("c@d.com", r.Options.MailtoCc);
        Assert.Equal("e@f.com", r.Options.MailtoBcc);
    }

    // geo subcommand
    [Fact]
    public void Parse_Geo_AllFields()
    {
        ArgParser.Result r = ArgParser.Parse([
            "geo", "--lat", "-41.2924", "--lon", "174.7787", "--query", "Wellington",
        ]);
        Assert.Equal(SubCommand.Geo, r.Options!.SubCommand);
        Assert.Equal(-41.2924, r.Options.GeoLat);
        Assert.Equal(174.7787, r.Options.GeoLon);
        Assert.Equal("Wellington", r.Options.GeoQuery);
    }

    [Fact]
    public void Parse_Geo_InvalidLat_Error()
    {
        ArgParser.Result r = ArgParser.Parse(["geo", "--lat", "notanumber", "--lon", "0"]);
        Assert.NotNull(r.Error);
    }

    // tel subcommand
    [Fact]
    public void Parse_Tel_NumberField()
    {
        ArgParser.Result r = ArgParser.Parse(["tel", "--number", "+15551234"]);
        Assert.Equal(SubCommand.Tel, r.Options!.SubCommand);
        Assert.Equal("+15551234", r.Options.TelNumber);
    }

    // Unknown flag inside subcommand
    [Fact]
    public void Parse_UnknownFlagInSubcommand_Error()
    {
        ArgParser.Result r = ArgParser.Parse(["wifi", "--unknown-flag", "x"]);
        Assert.NotNull(r.Error);
    }

    // Defaults
    [Fact]
    public void Parse_Defaults()
    {
        ArgParser.Result r = ArgParser.Parse(["hello"]);
        Assert.Equal(OutputFormat.Auto, r.Options!.Format);
        Assert.Equal(EccLevel.M, r.Options.Ecc);
        Assert.Equal(10, r.Options.PixelsPerModule);
        Assert.False(r.Options.NoMargin);
        Assert.False(r.Options.ForceBinary);
        Assert.Null(r.Options.OutputPath);
    }

    // Describe / version / help — ShellKit writes output to stdout during Parse and sets IsHandled=true.
    [Fact]
    public void Parse_Help_ReturnsIsHandled()
    {
        ArgParser.Result r = ArgParser.Parse(["--help"]);
        Assert.True(r.IsHandled);
        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public void Parse_Version_ReturnsIsHandled()
    {
        ArgParser.Result r = ArgParser.Parse(["--version"]);
        Assert.True(r.IsHandled);
    }

    [Fact]
    public void Parse_Describe_ReturnsIsHandled()
    {
        ArgParser.Result r = ArgParser.Parse(["--describe"]);
        Assert.True(r.IsHandled);
    }

    // Subcommand-scoped help: `qr wifi --help` should produce wifi-specific help (still IsHandled).
    [Fact]
    public void Parse_WifiHelp_ReturnsIsHandled()
    {
        ArgParser.Result r = ArgParser.Parse(["wifi", "--help"]);
        Assert.True(r.IsHandled);
    }

    // ── Round-1 review TA-I5: per-subcommand positional-rejection branches. Each helper
    //    rejects positional args with a specific error message; one test per dispatch site. ──

    [Fact]
    public void Parse_Wifi_WithPositional_Rejected()
    {
        ArgParser.Result r = ArgParser.Parse(["wifi", "stray", "--ssid", "Net"]);
        Assert.NotNull(r.Error);
        Assert.Contains("wifi does not accept positional", r.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_Sms_WithPositional_Rejected()
    {
        ArgParser.Result r = ArgParser.Parse(["sms", "stray", "--number", "+1555"]);
        Assert.NotNull(r.Error);
        Assert.Contains("sms does not accept positional", r.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_Mailto_WithPositional_Rejected()
    {
        ArgParser.Result r = ArgParser.Parse(["mailto", "stray", "--to", "a@b.com"]);
        Assert.NotNull(r.Error);
        Assert.Contains("mailto does not accept positional", r.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_Geo_WithPositional_Rejected()
    {
        ArgParser.Result r = ArgParser.Parse(["geo", "stray", "--lat", "0", "--lon", "0"]);
        Assert.NotNull(r.Error);
        Assert.Contains("geo does not accept positional", r.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_Tel_WithPositional_Rejected()
    {
        ArgParser.Result r = ArgParser.Parse(["tel", "stray", "--number", "+1555"]);
        Assert.NotNull(r.Error);
        Assert.Contains("tel does not accept positional", r.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_Text_TooManyPositionals_Rejected()
    {
        ArgParser.Result r = ArgParser.Parse(["one", "two"]);
        Assert.NotNull(r.Error);
        Assert.Contains("unexpected positional", r.Error!, StringComparison.Ordinal);
    }

    // ── Round-1 review SFH-I2 (CLI surface): --force flag is registered and parses ──
    [Fact]
    public void Parse_ForceFlag_OnText_SetsForceOverwrite()
    {
        ArgParser.Result r = ArgParser.Parse(["hello", "--force"]);
        Assert.NotNull(r.Options);
        Assert.True(r.Options!.ForceOverwrite);
    }

    [Fact]
    public void Parse_ForceFlag_OnHelper_SetsForceOverwrite()
    {
        ArgParser.Result r = ArgParser.Parse(["wifi", "--ssid", "Net", "--password", "pw", "--force"]);
        Assert.NotNull(r.Options);
        Assert.True(r.Options!.ForceOverwrite);
    }

    [Fact]
    public void Parse_NoForceFlag_DefaultsFalse()
    {
        ArgParser.Result r = ArgParser.Parse(["hello"]);
        Assert.NotNull(r.Options);
        Assert.False(r.Options!.ForceOverwrite);
    }
}
