#nullable enable
using System;
using System.IO;
using Xunit;
using Winix.Digest;

namespace Winix.Digest.Tests;

// Round-1 review I10 — pin the user-facing legacy-algorithm warning strings so a
// silent edit can't change what the user sees. Without this, a refactor that
// dropped one of the warnings (or weakened its wording) would land unnoticed.
public class AlgorithmWarningTests
{
    [Fact]
    public void Md5_ReturnsBrokenWarning()
    {
        string? warning = AlgorithmWarning.GetWarningOrNull(HashAlgorithm.Md5);
        Assert.NotNull(warning);
        Assert.Contains("MD5", warning, StringComparison.Ordinal);
        Assert.Contains("broken", warning, StringComparison.Ordinal);
        Assert.Contains("do not use for security", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Sha1_ReturnsCollisionWarning()
    {
        string? warning = AlgorithmWarning.GetWarningOrNull(HashAlgorithm.Sha1);
        Assert.NotNull(warning);
        Assert.Contains("SHA-1", warning, StringComparison.Ordinal);
        Assert.Contains("collision resistance", warning, StringComparison.Ordinal);
        // The wording deliberately distinguishes plain SHA-1 (broken) from HMAC-SHA-1
        // (still acceptable) — pin that distinction so it can't silently disappear.
        Assert.Contains("HMAC-SHA-1", warning, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HashAlgorithm.Sha256)]
    [InlineData(HashAlgorithm.Sha384)]
    [InlineData(HashAlgorithm.Sha512)]
    [InlineData(HashAlgorithm.Sha3_256)]
    [InlineData(HashAlgorithm.Sha3_512)]
    [InlineData(HashAlgorithm.Blake2b)]
    public void NonLegacy_ReturnsNull(HashAlgorithm algo)
    {
        Assert.Null(AlgorithmWarning.GetWarningOrNull(algo));
    }

    [Fact]
    public void EmitIfLegacy_WritesToStderr_ForMd5()
    {
        var stderr = new StringWriter();
        AlgorithmWarning.EmitIfLegacy(HashAlgorithm.Md5, stderr);
        Assert.Contains("MD5", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void EmitIfLegacy_WritesNothing_ForSha256()
    {
        var stderr = new StringWriter();
        AlgorithmWarning.EmitIfLegacy(HashAlgorithm.Sha256, stderr);
        Assert.Empty(stderr.ToString());
    }
}
