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

    [Fact]
    public void Username_with_colon_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => BasicAuthBuilder.Build("a:b", "pw"));
    }
}
