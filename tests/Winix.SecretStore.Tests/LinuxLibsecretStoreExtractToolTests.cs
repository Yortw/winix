#nullable enable
using System;
using Winix.SecretStore;
using Xunit;

namespace Winix.SecretStore.Tests;

/// <summary>
/// Pure unit tests for <see cref="LinuxNamespace.ExtractTool"/>. No secret-tool invocation,
/// so these run on every OS — the contract being tested is platform-agnostic string handling.
/// The helper lives outside the platform-gated <see cref="LinuxLibsecretStore"/> class for
/// exactly this reason.
/// </summary>
public class LinuxLibsecretStoreExtractToolTests
{
    [Fact]
    public void ExtractTool_SingleSlash_ReturnsPrefix()
    {
        Assert.Equal("envvault", LinuxNamespace.ExtractTool("envvault/github"));
    }

    [Fact]
    public void ExtractTool_MultipleSlashes_ReturnsFirstSegmentOnly()
    {
        Assert.Equal("envvault", LinuxNamespace.ExtractTool("envvault/github/sub"));
    }

    [Fact]
    public void ExtractTool_EmptyTail_AllowedBecauseOnlyPrefixMustBeNonEmpty()
    {
        Assert.Equal("a", LinuxNamespace.ExtractTool("a/"));
    }

    [Fact]
    public void ExtractTool_NoSlash_Throws()
    {
        Assert.Throws<ArgumentException>(() => LinuxNamespace.ExtractTool("envvault"));
    }

    [Fact]
    public void ExtractTool_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => LinuxNamespace.ExtractTool(""));
    }

    [Fact]
    public void ExtractTool_LeadingSlash_Throws()
    {
        Assert.Throws<ArgumentException>(() => LinuxNamespace.ExtractTool("/foo"));
    }
}
