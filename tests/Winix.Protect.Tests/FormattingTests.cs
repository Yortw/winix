#nullable enable
using Xunit;
using Winix.Protect;

namespace Winix.Protect.Tests;

public class FormattingTests
{
    [Fact]
    public void UsageError_Protect_Prefix()
    {
        Assert.Equal("protect: bad flag", Formatting.UsageError("protect", "bad flag"));
    }

    [Fact]
    public void UsageError_Unprotect_Prefix()
    {
        Assert.Equal("unprotect: bad flag", Formatting.UsageError("unprotect", "bad flag"));
    }

    [Fact]
    public void RuntimeError_IncludesPrefix()
    {
        Assert.Equal("protect: decryption failed", Formatting.RuntimeError("protect", "decryption failed"));
    }
}
