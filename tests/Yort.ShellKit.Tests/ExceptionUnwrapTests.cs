using Yort.ShellKit;
using Xunit;

namespace Yort.ShellKit.Tests;

/// <summary>
/// Unit tests for <see cref="ExceptionUnwrap"/>. Pins both the happy-path unwrap and the
/// depth-cap detection (the depthCapped flag surfaces when a TIE chain exceeds MaxDepth so
/// the caller can warn the displayed message may not be the genuine root cause). Migrated
/// from Winix.Wargs.Tests when the helper was consolidated into ShellKit.
/// </summary>
public class ExceptionUnwrapTests
{
    [Fact]
    public void UnwrapTypeInitWithDepth_NonTie_ReturnedUnchanged_NotCapped()
    {
        var ex = new System.InvalidOperationException("plain");
        var (surface, capped) = ExceptionUnwrap.UnwrapTypeInitWithDepth(ex);
        Assert.Same(ex, surface);
        Assert.False(capped);
    }

    [Fact]
    public void UnwrapTypeInitWithDepth_SingleTieWrapper_ReturnsInner_NotCapped()
    {
        var inner = new System.InvalidOperationException("real cause");
        var tie = new System.TypeInitializationException("SomeType", inner);
        var (surface, capped) = ExceptionUnwrap.UnwrapTypeInitWithDepth(tie);
        Assert.Same(inner, surface);
        Assert.False(capped);
    }

    [Fact]
    public void UnwrapTypeInitWithDepth_DeepChainBelowCap_FullyUnwrapped_NotCapped()
    {
        // 31 nested TIE wrappers — exactly at the cap boundary, MUST unwrap fully.
        System.Exception current = new System.InvalidOperationException("real cause");
        for (int i = 0; i < 31; i++)
        {
            current = new System.TypeInitializationException($"Type{i}", current);
        }
        var (surface, capped) = ExceptionUnwrap.UnwrapTypeInitWithDepth(current);
        Assert.IsType<System.InvalidOperationException>(surface);
        Assert.Equal("real cause", surface.Message);
        Assert.False(capped);
    }

    [Fact]
    public void UnwrapTypeInitWithDepth_ChainExceedingCap_StopsAtCap_FlagsCapped()
    {
        // 33 nested TIE wrappers — exceeds the 32-cap by one. The unwrap must stop at the
        // cap and signal depthCapped so the caller can append the "(unwrap depth limit
        // reached)" notice.
        System.Exception current = new System.InvalidOperationException("real cause buried at depth 33");
        for (int i = 0; i < 33; i++)
        {
            current = new System.TypeInitializationException($"Type{i}", current);
        }
        var (surface, capped) = ExceptionUnwrap.UnwrapTypeInitWithDepth(current);
        Assert.True(capped, "depth cap should be detected when chain exceeds MaxDepth");
        // Surface must still be a TIE (the cap stopped us mid-unwrap), not the real cause.
        Assert.IsType<System.TypeInitializationException>(surface);
    }

    [Fact]
    public void UnwrapTypeInitWithDepth_TieWithNullInner_ReturnsTie_NotCapped()
    {
        // Defensive: TIE constructed with null inner should be returned as-is (loop guard
        // is `tie.InnerException != null`, which evaluates false at iteration 0).
        var tie = new System.TypeInitializationException("SomeType", null);
        var (surface, capped) = ExceptionUnwrap.UnwrapTypeInitWithDepth(tie);
        Assert.Same(tie, surface);
        Assert.False(capped);
    }

    [Fact]
    public void UnwrapTypeInit_SimpleOverload_ReturnsSurfaceOnly()
    {
        // The bare overload (used by retry/nc/envvault) returns just the surfaced cause.
        var inner = new System.InvalidOperationException("real cause");
        var tie = new System.TypeInitializationException("SomeType", inner);
        System.Exception surface = ExceptionUnwrap.UnwrapTypeInit(tie);
        Assert.Same(inner, surface);
    }
}
