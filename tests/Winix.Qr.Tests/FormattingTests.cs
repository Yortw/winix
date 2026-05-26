#nullable enable
using Xunit;
using Winix.Qr;

namespace Winix.Qr.Tests;

public class FormattingTests
{
    [Fact]
    public void UsageError_PrependsQrColonSpace()
    {
        Assert.Equal("qr: unknown option: --foo", Formatting.UsageError("unknown option: --foo"));
    }

    [Fact]
    public void RuntimeError_PrependsQrColonSpace()
    {
        Assert.Equal("qr: payload is empty", Formatting.RuntimeError("payload is empty"));
    }

    [Fact]
    public void CapacityExceededHint_IncludesActionableSuggestion()
    {
        string msg = Formatting.CapacityExceededHint("H");
        Assert.Contains("-e l", msg);
        Assert.Contains("shorten", msg);
    }
}
