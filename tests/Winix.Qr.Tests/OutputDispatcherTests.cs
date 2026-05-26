#nullable enable
using Xunit;
using Winix.Qr;
using Winix.QrCode;

namespace Winix.Qr.Tests;

public class OutputDispatcherTests
{
    private static QrOptions Options(OutputFormat fmt) => new(
        SubCommand: SubCommand.Text,
        TextPayload: "hello",
        Format: fmt,
        PixelsPerModule: 4,
        Ecc: EccLevel.M,
        NoMargin: false,
        OutputPath: null,
        ForceBinary: false,
        ForceOverwrite: false,
        WifiSsid: null, WifiPassword: null, WifiSecurity: null, WifiHidden: false,
        SmsNumber: null, SmsMessage: null,
        MailtoTo: null, MailtoSubject: null, MailtoBody: null, MailtoCc: null, MailtoBcc: null,
        GeoLat: null, GeoLon: null, GeoQuery: null,
        TelNumber: null);

    [Fact]
    public void Dispatch_Auto_Tty_ReturnsUnicodeString()
    {
        OutputDispatcher.Result r = OutputDispatcher.Dispatch("hello", Options(OutputFormat.Auto), stdoutIsTty: true);
        Assert.NotNull(r.Text);
        Assert.Null(r.Bytes);
        Assert.Contains('█', r.Text!);      // unicode full-block present
    }

    [Fact]
    public void Dispatch_Auto_Piped_ReturnsSvgString()
    {
        OutputDispatcher.Result r = OutputDispatcher.Dispatch("hello", Options(OutputFormat.Auto), stdoutIsTty: false);
        Assert.NotNull(r.Text);
        Assert.Contains("<svg", r.Text!);
    }

    [Fact]
    public void Dispatch_ExplicitUnicode_OverridesTtyCheck()
    {
        OutputDispatcher.Result r = OutputDispatcher.Dispatch("hello", Options(OutputFormat.Unicode), stdoutIsTty: false);
        Assert.Contains('█', r.Text!);
    }

    [Fact]
    public void Dispatch_ExplicitSvg_OverridesTtyCheck()
    {
        OutputDispatcher.Result r = OutputDispatcher.Dispatch("hello", Options(OutputFormat.Svg), stdoutIsTty: true);
        Assert.Contains("<svg", r.Text!);
    }

    [Fact]
    public void Dispatch_ExplicitAscii()
    {
        OutputDispatcher.Result r = OutputDispatcher.Dispatch("hello", Options(OutputFormat.Ascii), stdoutIsTty: true);
        Assert.NotNull(r.Text);
        Assert.Contains("##", r.Text!);
    }

    [Fact]
    public void Dispatch_Png_ReturnsBytes()
    {
        OutputDispatcher.Result r = OutputDispatcher.Dispatch("hello", Options(OutputFormat.Png), stdoutIsTty: false);
        Assert.Null(r.Text);
        Assert.NotNull(r.Bytes);
        Assert.Equal(0x89, r.Bytes![0]);    // PNG magic byte 0
    }

    [Fact]
    public void Dispatch_Png_NoMargin_RespectedInBytes()
    {
        OutputDispatcher.Result withMargin = OutputDispatcher.Dispatch("hello",
            Options(OutputFormat.Png) with { NoMargin = false }, stdoutIsTty: false);
        OutputDispatcher.Result noMargin = OutputDispatcher.Dispatch("hello",
            Options(OutputFormat.Png) with { NoMargin = true }, stdoutIsTty: false);
        Assert.NotEqual(withMargin.Bytes!.Length, noMargin.Bytes!.Length);
    }

    // --output extension sniffing (Auto format only)

    [Fact]
    public void Dispatch_Auto_OutputPathEndsPng_SelectsPng()
    {
        OutputDispatcher.Result r = OutputDispatcher.Dispatch("hello",
            Options(OutputFormat.Auto) with { OutputPath = "code.png" }, stdoutIsTty: true);
        Assert.NotNull(r.Bytes);
        Assert.Null(r.Text);
        Assert.Equal(0x89, r.Bytes![0]);
    }

    [Fact]
    public void Dispatch_Auto_OutputPathEndsSvg_SelectsSvg()
    {
        OutputDispatcher.Result r = OutputDispatcher.Dispatch("hello",
            Options(OutputFormat.Auto) with { OutputPath = "code.svg" }, stdoutIsTty: true);
        Assert.NotNull(r.Text);
        Assert.Contains("<svg", r.Text!);
    }

    [Fact]
    public void Dispatch_Auto_OutputPathExtensionCaseInsensitive()
    {
        OutputDispatcher.Result r = OutputDispatcher.Dispatch("hello",
            Options(OutputFormat.Auto) with { OutputPath = "C:\\temp\\code.PNG" }, stdoutIsTty: true);
        Assert.NotNull(r.Bytes);
        Assert.Equal(0x89, r.Bytes![0]);
    }

    [Fact]
    public void Dispatch_Auto_OutputPathUnknownExtension_FallsBackToStreamDefault()
    {
        // Unknown extension + TTY → unicode (same as no --output).
        OutputDispatcher.Result r = OutputDispatcher.Dispatch("hello",
            Options(OutputFormat.Auto) with { OutputPath = "code.txt" }, stdoutIsTty: true);
        Assert.NotNull(r.Text);
        Assert.Contains('█', r.Text!);
    }

    [Fact]
    public void Dispatch_ExplicitFormat_OverridesOutputExtension()
    {
        // User passed --format unicode explicitly; the .png extension must NOT override.
        OutputDispatcher.Result r = OutputDispatcher.Dispatch("hello",
            Options(OutputFormat.Unicode) with { OutputPath = "code.png" }, stdoutIsTty: true);
        Assert.NotNull(r.Text);
        Assert.Contains('█', r.Text!);
    }
}
