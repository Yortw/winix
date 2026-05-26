#nullable enable

using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

/// <summary>
/// R5 contract pins for <see cref="SchtasksBackend.NormaliseFolderForQuery"/>. The reason
/// this helper exists at all is that R4 I3+I4 inadvertently made the default-folder
/// `schedule list` invocation worse: pre-R4 both `\Winix` and `\Winix\` collapsed to an
/// empty list, but post-R4 the trailing-backslash case surfaced 'filename syntax is
/// incorrect' to the user as an Unavailable error. Trimming brings the default folder
/// path through the IsBenignSchtasksEmpty pattern set as intended.
/// </summary>
public sealed class SchtasksBackendNormaliseFolderTests
{
    [Fact]
    public void Null_ReturnsDefault()
    {
        Assert.Equal(@"\Winix", SchtasksBackend.NormaliseFolderForQuery(null));
    }

    [Fact]
    public void Empty_ReturnsAsIs()
    {
        // Empty string with no backslash to trim — pass through. Note Program.cs is
        // expected to substitute null for empty before calling, so this case is
        // primarily a defensive contract.
        Assert.Equal("", SchtasksBackend.NormaliseFolderForQuery(""));
    }

    [Fact]
    public void DefaultFolderWithTrailingBackslash_Stripped()
    {
        Assert.Equal(@"\Winix", SchtasksBackend.NormaliseFolderForQuery(@"\Winix\"));
    }

    [Fact]
    public void DefaultFolderWithoutTrailingBackslash_PassedThrough()
    {
        Assert.Equal(@"\Winix", SchtasksBackend.NormaliseFolderForQuery(@"\Winix"));
    }

    [Fact]
    public void NestedFolderWithTrailingBackslash_Stripped()
    {
        Assert.Equal(@"\Winix\Tools", SchtasksBackend.NormaliseFolderForQuery(@"\Winix\Tools\"));
    }

    [Fact]
    public void MultipleTrailingBackslashes_AllStripped()
    {
        // Defensive: a user manually passing --folder '\Foo\\\' should not propagate
        // the noise to schtasks.
        Assert.Equal(@"\Foo", SchtasksBackend.NormaliseFolderForQuery(@"\Foo\\\"));
    }
}
