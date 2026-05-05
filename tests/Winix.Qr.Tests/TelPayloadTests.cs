#nullable enable
using System;
using Xunit;
using Winix.Qr.Helpers;

namespace Winix.Qr.Tests;

public class TelPayloadTests
{
    [Fact]
    public void Build_BasicNumber()
    {
        Assert.Equal("tel:+15551234567", TelPayload.Build("+15551234567"));
    }

    [Fact]
    public void Build_NumberWithSeparators_Preserved()
    {
        // tel: URI allows punctuation in the number — we pass it through as-is.
        Assert.Equal("tel:+1-555-123-4567", TelPayload.Build("+1-555-123-4567"));
    }

    [Fact]
    public void Build_EmptyNumber_Throws()
    {
        Assert.Throws<ArgumentException>(() => TelPayload.Build(""));
    }

    // ── Round-1 review SFH-I3: phone-number sanitisation ──

    [Fact]
    public void Build_NumberWithSpaces_Stripped()
    {
        // Spaces are common copy/paste artefacts; not part of any URI scheme.
        Assert.Equal("tel:+15551234567", TelPayload.Build("+1 555 1234567"));
    }

    [Fact]
    public void Build_NumberWithLetters_Throws()
    {
        // Pre-fix: `tel --number "+1 555 abc"` silently produced an unscannable URI.
        Assert.Throws<ArgumentException>(() => TelPayload.Build("+1555abc"));
    }

    [Fact]
    public void Build_NumberOnlyDigitsNoPlus_Accepted()
    {
        // E.164 leading-+ is recommended but not required by RFC 3966.
        Assert.Equal("tel:5551234567", TelPayload.Build("5551234567"));
    }

    [Fact]
    public void Build_NumberWithParens_Accepted()
    {
        Assert.Equal("tel:+1(555)123-4567", TelPayload.Build("+1(555)123-4567"));
    }

    [Fact]
    public void Build_NumberWithExtParam_Accepted()
    {
        // RFC 3966 §3 extension parameter.
        Assert.Equal("tel:+15551234567;ext=42", TelPayload.Build("+15551234567;ext=42"));
    }

    [Fact]
    public void Build_ParamWithDisallowedChar_Throws()
    {
        // SFH-I3: parameter chars are limited to alnum + '=', '.', '-', '_'. A '<' is rejected.
        Assert.Throws<ArgumentException>(() => TelPayload.Build("+15551234567;ext=<script>"));
    }

    [Fact]
    public void Build_NoDigits_Throws()
    {
        Assert.Throws<ArgumentException>(() => TelPayload.Build("---"));
    }

    [Fact]
    public void Build_OnlyWhitespace_Throws()
    {
        Assert.Throws<ArgumentException>(() => TelPayload.Build("   "));
    }
}
