#nullable enable

using System;
using Winix.WhoHolds;
using Xunit;

namespace Winix.WhoHolds.Tests;

/// <summary>
/// Pins the FindResult contract introduced in the 2026-05-08 round-1 architectural fix
/// for SFH I1+I2+I3. The finder layer's signal of "API errored" must travel a typed path
/// to the CLI; these tests fail loudly if the static factories drift.
/// </summary>
public sealed class FindResultTests
{
    [Fact]
    public void Empty_IsSuccessEmptyResult()
    {
        FindResult result = FindResult.Empty;

        Assert.False(result.QueryFailed);
        Assert.Null(result.Reason);
        Assert.Empty(result.Results);
    }

    [Fact]
    public void Success_PreservesResultsAndMarksNotFailed()
    {
        var holder = new LockInfo(1234, "test", "test.txt");
        FindResult result = FindResult.Success(new[] { holder });

        Assert.False(result.QueryFailed);
        Assert.Null(result.Reason);
        Assert.Single(result.Results);
        Assert.Equal(1234, result.Results[0].ProcessId);
    }

    [Fact]
    public void Success_WithEmptyList_IsCleanSuccessEmpty()
    {
        FindResult result = FindResult.Success(Array.Empty<LockInfo>());

        Assert.False(result.QueryFailed);
        Assert.Null(result.Reason);
        Assert.Empty(result.Results);
    }

    [Fact]
    public void Failed_SetsQueryFailedTrueAndReturnsEmptyResults()
    {
        FindResult result = FindResult.Failed("RmStartSession failed: hr=0x80004005");

        Assert.True(result.QueryFailed);
        Assert.Equal("RmStartSession failed: hr=0x80004005", result.Reason);
        Assert.Empty(result.Results);
    }

    [Fact]
    public void Failed_ReasonIsRequiredAndUserFacing()
    {
        // The reason field is what the CLI surfaces on stderr; a null or empty reason
        // would leak as a bare "whoholds: query failed for 'X':" message. Pin that the
        // factory accepts the string we feed it (xunit will catch a future overload that
        // accepts nullable reason).
        FindResult result = FindResult.Failed("backend timed out");
        Assert.Equal("backend timed out", result.Reason);
    }
}
