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
}
