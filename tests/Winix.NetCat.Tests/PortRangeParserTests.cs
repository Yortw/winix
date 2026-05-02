#nullable enable

using System.Collections.Generic;
using Winix.NetCat;
using Xunit;

namespace Winix.NetCat.Tests;

public sealed class PortRangeParserTests
{
    [Fact]
    public void Parse_SinglePort_ReturnsOneRange()
    {
        IReadOnlyList<PortRange> ranges = PortRangeParser.Parse("80");

        Assert.Single(ranges);
        Assert.Equal(80, ranges[0].Low);
        Assert.Equal(80, ranges[0].High);
    }

    [Fact]
    public void Parse_Range_ReturnsOneRange()
    {
        IReadOnlyList<PortRange> ranges = PortRangeParser.Parse("80-100");

        Assert.Single(ranges);
        Assert.Equal(80, ranges[0].Low);
        Assert.Equal(100, ranges[0].High);
    }

    [Fact]
    public void Parse_List_ReturnsMultipleSinglePortRanges()
    {
        IReadOnlyList<PortRange> ranges = PortRangeParser.Parse("80,443,8080");

        Assert.Equal(3, ranges.Count);
        Assert.Equal(new PortRange(80), ranges[0]);
        Assert.Equal(new PortRange(443), ranges[1]);
        Assert.Equal(new PortRange(8080), ranges[2]);
    }

    [Fact]
    public void Parse_Mixed_ReturnsCorrectRanges()
    {
        IReadOnlyList<PortRange> ranges = PortRangeParser.Parse("80-100,443,8080-8090");

        Assert.Equal(3, ranges.Count);
        Assert.Equal(new PortRange(80, 100), ranges[0]);
        Assert.Equal(new PortRange(443), ranges[1]);
        Assert.Equal(new PortRange(8080, 8090), ranges[2]);
    }

    [Fact]
    public void Parse_SegmentsWithWhitespace_AreTolerated()
    {
        IReadOnlyList<PortRange> ranges = PortRangeParser.Parse("80, 443 , 8080-8090");

        Assert.Equal(3, ranges.Count);
        Assert.Equal(new PortRange(80), ranges[0]);
        Assert.Equal(new PortRange(443), ranges[1]);
        Assert.Equal(new PortRange(8080, 8090), ranges[2]);
    }

    [Fact]
    public void Parse_Empty_Throws()
    {
        Assert.Throws<FormatException>(() => PortRangeParser.Parse(""));
    }

    [Fact]
    public void Parse_Whitespace_Throws()
    {
        Assert.Throws<FormatException>(() => PortRangeParser.Parse("   "));
    }

    [Fact]
    public void Parse_NonNumeric_Throws()
    {
        Assert.Throws<FormatException>(() => PortRangeParser.Parse("abc"));
    }

    [Fact]
    public void Parse_PortZero_Throws()
    {
        Assert.Throws<FormatException>(() => PortRangeParser.Parse("0"));
    }

    [Fact]
    public void Parse_PortTooLarge_Throws()
    {
        Assert.Throws<FormatException>(() => PortRangeParser.Parse("70000"));
    }

    [Fact]
    public void Parse_DescendingRange_Throws()
    {
        Assert.Throws<FormatException>(() => PortRangeParser.Parse("100-80"));
    }

    /// <summary>
    /// Round-10 review I-3: pin the contract for malformed range edges that prior rounds
    /// missed. Specifically:
    /// <list type="bullet">
    ///   <item><c>"-1"</c> — leading dash with empty low bound. ParsePort("") fails.</item>
    ///   <item><c>"80-"</c> — trailing dash with empty high bound. ParsePort("") fails.</item>
    ///   <item><c>"-"</c> — both bounds empty. ParsePort("") fails.</item>
    /// </list>
    /// All three must surface as <see cref="FormatException"/> from <c>Parse</c>, not silently
    /// produce a degenerate range or escape as a different exception type that the CLI wrapper
    /// would mis-attribute as <c>unexpected_error</c>.
    /// </summary>
    [Theory]
    [InlineData("-1")]
    [InlineData("80-")]
    [InlineData("-")]
    public void Parse_MalformedRangeEdges_Throws(string specifier)
    {
        Assert.Throws<FormatException>(() => PortRangeParser.Parse(specifier));
    }

    /// <summary>
    /// Round-10 review I-3: pin the contract for leading-zero ports. <c>int.TryParse</c>
    /// accepts <c>"01"</c> as <c>1</c>, so <c>"01-10"</c> parses as the range <c>(1, 10)</c>.
    /// This is permissive but documented behaviour — pin it so a future tightening to
    /// "leading zeros are a syntax error" is a deliberate contract change with a failing
    /// test, not a silent regression.
    /// </summary>
    [Fact]
    public void Parse_LeadingZeroPort_AcceptedAsCanonicalValue()
    {
        IReadOnlyList<PortRange> ranges = PortRangeParser.Parse("01-10");

        Assert.Single(ranges);
        Assert.Equal(1, ranges[0].Low);
        Assert.Equal(10, ranges[0].High);
    }
}
