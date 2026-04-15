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
}
