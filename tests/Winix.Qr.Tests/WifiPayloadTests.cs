#nullable enable
using System;
using Xunit;
using Winix.Qr.Helpers;

namespace Winix.Qr.Tests;

public class WifiPayloadTests
{
    [Fact]
    public void Build_Wpa2_WithPassword_FormatsAllFields()
    {
        string p = WifiPayload.Build(ssid: "HomeNet", password: "s3cr3t", security: "wpa2", hidden: false);
        Assert.Equal("WIFI:T:WPA;S:HomeNet;P:s3cr3t;;", p);
    }

    [Fact]
    public void Build_Wpa_MapsToWpa()
    {
        string p = WifiPayload.Build(ssid: "Net", password: "pw", security: "wpa", hidden: false);
        Assert.Equal("WIFI:T:WPA;S:Net;P:pw;;", p);
    }

    [Fact]
    public void Build_Wep_MapsToWep()
    {
        string p = WifiPayload.Build(ssid: "Net", password: "pw", security: "wep", hidden: false);
        Assert.Equal("WIFI:T:WEP;S:Net;P:pw;;", p);
    }

    [Fact]
    public void Build_Nopass_OmitsPasswordField()
    {
        string p = WifiPayload.Build(ssid: "Guest", password: null, security: "nopass", hidden: false);
        Assert.Equal("WIFI:T:nopass;S:Guest;;", p);
    }

    [Fact]
    public void Build_Hidden_IncludesHFlag()
    {
        string p = WifiPayload.Build(ssid: "Stealth", password: "pw", security: "wpa2", hidden: true);
        Assert.Equal("WIFI:T:WPA;S:Stealth;P:pw;H:true;;", p);
    }

    [Theory]
    [InlineData(":",     @"\:")]
    [InlineData(";",     @"\;")]
    [InlineData(",",     @"\,")]
    [InlineData("\"",    "\\\"")]
    [InlineData("\\",    @"\\")]
    public void Build_EscapesSpecialCharactersInPassword(string input, string escaped)
    {
        string p = WifiPayload.Build(ssid: "Net", password: $"a{input}b", security: "wpa2", hidden: false);
        Assert.Equal($"WIFI:T:WPA;S:Net;P:a{escaped}b;;", p);
    }

    [Fact]
    public void Build_EscapesInSsid()
    {
        string p = WifiPayload.Build(ssid: "Net;work", password: "pw", security: "wpa2", hidden: false);
        Assert.Equal(@"WIFI:T:WPA;S:Net\;work;P:pw;;", p);
    }

    [Fact]
    public void Build_EmptySsid_Throws()
    {
        Assert.Throws<ArgumentException>(() => WifiPayload.Build(
            ssid: "", password: "pw", security: "wpa2", hidden: false));
    }

    [Fact]
    public void Build_UnknownSecurity_Throws()
    {
        Assert.Throws<ArgumentException>(() => WifiPayload.Build(
            ssid: "Net", password: "pw", security: "enterprise", hidden: false));
    }

    [Fact]
    public void Build_Wpa2WithoutPassword_Throws()
    {
        // WPA/WEP/WPA2 require a password — 'nopass' is the only security that skips it.
        Assert.Throws<ArgumentException>(() => WifiPayload.Build(
            ssid: "Net", password: null, security: "wpa2", hidden: false));
    }
}
