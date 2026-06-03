using Winix.MkAuth;
using Xunit;

public class BearerAuthBuilderTests
{
    [Fact]
    public void Builds_bearer_header()
    {
        var r = BearerAuthBuilder.Build("abc.def.ghi");
        Assert.Equal("Authorization", r.HeaderName);
        Assert.Equal("Bearer abc.def.ghi", r.HeaderValue);
    }
}
