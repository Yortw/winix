using Winix.MkAuth;
using Xunit;

public class BasicAuthBuilderTests
{
    [Fact]
    public void Builds_rfc7617_header()
    {
        // base64("Aladdin:open sesame") == "QWxhZGRpbjpvcGVuIHNlc2FtZQ==" (RFC 7617 example)
        var r = BasicAuthBuilder.Build("Aladdin", "open sesame");
        Assert.Equal("Authorization", r.HeaderName);
        Assert.Equal("Basic QWxhZGRpbjpvcGVuIHNlc2FtZQ==", r.HeaderValue);
    }

    [Fact] // B5: library-level backstop now throws MkAuthException (single-arg), not the banned two-arg
    public void Username_with_colon_is_rejected()    // ArgumentException(msg, paramName) form (SR-key leak class).
    {
        var ex = Assert.Throws<MkAuthException>(() => BasicAuthBuilder.Build("a:b", "pw"));
        Assert.Contains("colon", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
