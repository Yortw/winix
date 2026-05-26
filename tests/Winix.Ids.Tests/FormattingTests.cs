using System;
using Xunit;
using Winix.Ids;

namespace Winix.Ids.Tests;

public class FormattingTests
{
    // Known input — a deliberately mixed-hex GUID so case transforms are observable.
    private static readonly Guid Sample = Guid.Parse("018e7f6f-a7a9-7c3e-8a1b-d0e8f3c94a5d");

    [Theory]
    [InlineData(UuidFormat.Default, false, "018e7f6f-a7a9-7c3e-8a1b-d0e8f3c94a5d")]
    [InlineData(UuidFormat.Default, true,  "018E7F6F-A7A9-7C3E-8A1B-D0E8F3C94A5D")]
    [InlineData(UuidFormat.Hex,     false, "018e7f6fa7a97c3e8a1bd0e8f3c94a5d")]
    [InlineData(UuidFormat.Hex,     true,  "018E7F6FA7A97C3E8A1BD0E8F3C94A5D")]
    [InlineData(UuidFormat.Braces,  false, "{018e7f6f-a7a9-7c3e-8a1b-d0e8f3c94a5d}")]
    [InlineData(UuidFormat.Braces,  true,  "{018E7F6F-A7A9-7C3E-8A1B-D0E8F3C94A5D}")]
    [InlineData(UuidFormat.Urn,     false, "urn:uuid:018e7f6f-a7a9-7c3e-8a1b-d0e8f3c94a5d")]
    [InlineData(UuidFormat.Urn,     true,  "urn:uuid:018E7F6F-A7A9-7C3E-8A1B-D0E8F3C94A5D")]
    public void FormatGuid_AllShapeCaseCombinations(UuidFormat format, bool uppercase, string expected)
    {
        Assert.Equal(expected, Formatting.FormatGuid(Sample, format, uppercase));
    }
}
