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
}
