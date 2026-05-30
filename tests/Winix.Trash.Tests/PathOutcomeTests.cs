#nullable enable
using System;
using Winix.Trash;
using Xunit;

namespace Winix.Trash.Tests;

public class PathOutcomeTests
{
    [Fact]
    public void Ok_succeeds_with_no_error()
    {
        var o = PathOutcome.Ok("/x");
        Assert.True(o.Succeeded);
        Assert.Null(o.Error);
        Assert.Equal("/x", o.Path);
    }

    [Fact]
    public void Failed_is_not_succeeded_and_carries_the_reason()
    {
        var o = PathOutcome.Failed("/x", "boom");
        Assert.False(o.Succeeded);
        Assert.Equal("boom", o.Error);
        Assert.Equal("/x", o.Path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_rejects_a_blank_nonnull_error(string blank)
    {
        // The illegal "failure with a blank reason" state must be unrepresentable — it would count as
        // a failure in SuccessCount while printing an empty reason line.
        Assert.Throws<ArgumentException>(() => new PathOutcome("/x", blank));
    }

    [Fact]
    public void SuccessCount_and_AnyFailed_track_outcomes()
    {
        var r = new TrashResult
        {
            Outcomes = new[] { PathOutcome.Ok("/a"), PathOutcome.Failed("/b", "no") },
        };

        Assert.Equal(1, r.SuccessCount);
        Assert.True(r.AnyFailed);
    }
}
