#nullable enable
using System;
using Xunit;
using Winix.Qr.Helpers;

namespace Winix.Qr.Tests;

public class GeoPayloadTests
{
    [Fact]
    public void Build_LatLon_NoQuery()
    {
        Assert.Equal("geo:-41.2924,174.7787", GeoPayload.Build(-41.2924, 174.7787, null));
    }

    [Fact]
    public void Build_LatLonQuery()
    {
        Assert.Equal("geo:-41.2924,174.7787?q=Wellington", GeoPayload.Build(-41.2924, 174.7787, "Wellington"));
    }

    [Fact]
    public void Build_QueryWithSpaces_PercentEncoded()
    {
        Assert.Equal("geo:0,0?q=Wellington%20NZ", GeoPayload.Build(0, 0, "Wellington NZ"));
    }

    [Theory]
    [InlineData(-90.1, 0)]
    [InlineData(90.1, 0)]
    [InlineData(0, -180.1)]
    [InlineData(0, 180.1)]
    public void Build_LatLonOutOfRange_Throws(double lat, double lon)
    {
        Assert.Throws<ArgumentException>(() => GeoPayload.Build(lat, lon, null));
    }

    [Fact]
    public void Build_FormatUsesInvariantCulture()
    {
        // Regression: cultures with ',' as decimal separator must not produce "geo:41,2924,174,7787".
        System.Globalization.CultureInfo original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("geo:41.2924,174.7787", GeoPayload.Build(41.2924, 174.7787, null));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }
}
