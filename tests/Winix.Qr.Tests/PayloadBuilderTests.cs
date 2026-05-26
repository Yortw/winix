#nullable enable
using System;
using Xunit;
using Winix.Qr;
using Winix.QrCode;

namespace Winix.Qr.Tests;

public class PayloadBuilderTests
{
    // Minimal QrOptions factory — every helper test creates a base and overrides fields.
    private static QrOptions Base(SubCommand sc) => new(
        SubCommand: sc,
        TextPayload: null,
        Format: OutputFormat.Auto,
        PixelsPerModule: 10,
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
    public void Build_Text_PassesThrough()
    {
        QrOptions o = Base(SubCommand.Text) with { TextPayload = "hello" };
        Assert.Equal("hello", PayloadBuilder.Build(o));
    }

    [Fact]
    public void Build_TextWithNullPayload_Throws()
    {
        QrOptions o = Base(SubCommand.Text);    // TextPayload stays null
        Assert.Throws<InvalidOperationException>(() => PayloadBuilder.Build(o));
    }

    [Fact]
    public void Build_Wifi_ForwardsToWifiPayload()
    {
        QrOptions o = Base(SubCommand.Wifi) with
        {
            WifiSsid = "HomeNet",
            WifiPassword = "s3cr3t",
            WifiSecurity = "wpa2",
            WifiHidden = false,
        };
        Assert.Equal("WIFI:T:WPA;S:HomeNet;P:s3cr3t;;", PayloadBuilder.Build(o));
    }

    [Fact]
    public void Build_Wifi_MissingSsid_Throws()
    {
        QrOptions o = Base(SubCommand.Wifi) with
        {
            WifiSsid = null,
            WifiPassword = "s3cr3t",
            WifiSecurity = "wpa2",
        };
        Assert.Throws<InvalidOperationException>(() => PayloadBuilder.Build(o));
    }

    [Fact]
    public void Build_Sms_ForwardsToSmsPayload()
    {
        QrOptions o = Base(SubCommand.Sms) with { SmsNumber = "+15551234", SmsMessage = "Hi" };
        Assert.Equal("sms:+15551234?body=Hi", PayloadBuilder.Build(o));
    }

    [Fact]
    public void Build_Sms_MissingNumber_Throws()
    {
        QrOptions o = Base(SubCommand.Sms) with { SmsMessage = "Hi" };
        Assert.Throws<InvalidOperationException>(() => PayloadBuilder.Build(o));
    }

    [Fact]
    public void Build_Mailto_ForwardsToMailtoPayload()
    {
        QrOptions o = Base(SubCommand.Mailto) with { MailtoTo = "a@b.com", MailtoSubject = "Hi" };
        Assert.Equal("mailto:a@b.com?subject=Hi", PayloadBuilder.Build(o));
    }

    [Fact]
    public void Build_Geo_ForwardsToGeoPayload()
    {
        QrOptions o = Base(SubCommand.Geo) with { GeoLat = 0, GeoLon = 0, GeoQuery = "origin" };
        Assert.Equal("geo:0,0?q=origin", PayloadBuilder.Build(o));
    }

    [Fact]
    public void Build_Geo_MissingLat_Throws()
    {
        QrOptions o = Base(SubCommand.Geo) with { GeoLon = 0 };
        Assert.Throws<InvalidOperationException>(() => PayloadBuilder.Build(o));
    }

    [Fact]
    public void Build_Tel_ForwardsToTelPayload()
    {
        QrOptions o = Base(SubCommand.Tel) with { TelNumber = "+15551234567" };
        Assert.Equal("tel:+15551234567", PayloadBuilder.Build(o));
    }

    [Fact]
    public void Build_Tel_MissingNumber_Throws()
    {
        QrOptions o = Base(SubCommand.Tel);
        Assert.Throws<InvalidOperationException>(() => PayloadBuilder.Build(o));
    }
}
