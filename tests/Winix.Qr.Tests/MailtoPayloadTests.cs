#nullable enable
using System;
using Xunit;
using Winix.Qr.Helpers;

namespace Winix.Qr.Tests;

public class MailtoPayloadTests
{
    [Fact]
    public void Build_ToOnly()
    {
        Assert.Equal("mailto:a@b.com", MailtoPayload.Build("a@b.com", null, null, null, null));
    }

    [Fact]
    public void Build_ToAndSubject()
    {
        Assert.Equal("mailto:a@b.com?subject=Hi", MailtoPayload.Build("a@b.com", "Hi", null, null, null));
    }

    [Fact]
    public void Build_AllFields()
    {
        Assert.Equal(
            "mailto:a@b.com?subject=Hi&body=hello&cc=c%40d.com&bcc=e%40f.com",
            MailtoPayload.Build("a@b.com", "Hi", "hello", "c@d.com", "e@f.com"));
    }

    [Fact]
    public void Build_SubjectWithSpaces_PercentEncoded()
    {
        Assert.Equal("mailto:a@b.com?subject=Bug%20report", MailtoPayload.Build("a@b.com", "Bug report", null, null, null));
    }

    [Fact]
    public void Build_BodyWithAmpersand_PercentEncoded()
    {
        Assert.Equal("mailto:a@b.com?body=x%26y", MailtoPayload.Build("a@b.com", null, "x&y", null, null));
    }

    [Fact]
    public void Build_EmptyTo_Throws()
    {
        Assert.Throws<ArgumentException>(() => MailtoPayload.Build("", null, null, null, null));
    }

    // ── Round-3 review TA-I1: regression detector for the InvariantGlobalization-induced
    //    'Arg_ParamName_Name' resource-key leak. The test project sets
    //    InvariantGlobalization=true, so reverting MailtoPayload to the two-arg
    //    ArgumentException(message, paramName) constructor will cause Message to contain
    //    the SR token. Without this test, a future regression in MailtoPayload would slip
    //    silently — the current Build_EmptyTo_Throws asserts only the exception type, not
    //    the message content. ──
    [Fact]
    public void Build_EmptyTo_ErrorMessageDoesNotContainResourceKey()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => MailtoPayload.Build("", null, null, null, null));
        Assert.DoesNotContain("Arg_ParamName_Name", ex.Message, StringComparison.Ordinal);
    }
}
