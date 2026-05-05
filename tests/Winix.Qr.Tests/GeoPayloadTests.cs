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

    // ── Round-3 review TA-I1: regression detector for the InvariantGlobalization-induced
    //    'Arg_ParamName_Name' resource-key leak. Both lat and lon validation sites are
    //    covered. See MailtoPayloadTests for full rationale. ──
    [Fact]
    public void Build_LatOutOfRange_ErrorMessageDoesNotContainResourceKey()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => GeoPayload.Build(91, 0, null));
        Assert.DoesNotContain("Arg_ParamName_Name", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_LonOutOfRange_ErrorMessageDoesNotContainResourceKey()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => GeoPayload.Build(0, 181, null));
        Assert.DoesNotContain("Arg_ParamName_Name", ex.Message, StringComparison.Ordinal);
    }

    // ── Round-1 review TA-I4: pin the exact boundary values so an off-by-one
    //    in the validator (e.g. `>` vs `>=`) is caught explicitly. ──
    [Theory]
    [InlineData(-90.0, -180.0)]
    [InlineData(-90.0, 180.0)]
    [InlineData(90.0, -180.0)]
    [InlineData(90.0, 180.0)]
    [InlineData(0.0, 0.0)]
    public void Build_LatLonAtBoundary_Accepted(double lat, double lon)
    {
        // Exact boundary values are inclusive — must NOT throw.
        string result = GeoPayload.Build(lat, lon, null);
        Assert.StartsWith("geo:", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_FormatUsesInvariantCulture()
    {
        // Regression: cultures with ',' as decimal separator must not produce "geo:41,2924,174,7787".
        // Round-2 review: this test originally swapped CurrentCulture to de-DE, but Winix.Qr.Tests
        // now sets InvariantGlobalization=true (mirroring qr.csproj) and CultureInfo("de-DE") cannot
        // even be constructed in invariant mode. The point of the test is to pin the formatter's
        // OUTPUT, which is unconditionally invariant-formatted via string.Create(InvariantCulture).
        // A plain assertion on the output is the test that actually pins the contract under
        // production runtime config.
        Assert.Equal("geo:41.2924,174.7787", GeoPayload.Build(41.2924, 174.7787, null));
    }
}
