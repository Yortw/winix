#nullable enable
using System;
using Xunit;
using Winix.Qr.Helpers;

namespace Winix.Qr.Tests;

public class SmsPayloadTests
{
    [Fact]
    public void Build_NumberAndMessage()
    {
        Assert.Equal("sms:+15551234567?body=Hello", SmsPayload.Build("+15551234567", "Hello"));
    }

    [Fact]
    public void Build_NumberOnly_NoBody()
    {
        Assert.Equal("sms:+15551234567", SmsPayload.Build("+15551234567", null));
    }

    [Fact]
    public void Build_MessageWithSpaces_PercentEncoded()
    {
        Assert.Equal("sms:+15551234567?body=hello%20world", SmsPayload.Build("+15551234567", "hello world"));
    }

    [Fact]
    public void Build_MessageWithSpecialChars_PercentEncoded()
    {
        Assert.Equal("sms:+15551234567?body=a%3Db%26c", SmsPayload.Build("+15551234567", "a=b&c"));
    }

    [Fact]
    public void Build_EmptyNumber_Throws()
    {
        Assert.Throws<ArgumentException>(() => SmsPayload.Build("", "Hi"));
    }
}
