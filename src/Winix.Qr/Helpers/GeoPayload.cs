#nullable enable
using System;
using System.Globalization;

namespace Winix.Qr.Helpers;

/// <summary>Builds a <c>geo:</c> URI per RFC 5870.</summary>
public static class GeoPayload
{
    /// <summary>Build the geo URI.</summary>
    /// <param name="lat">Latitude in [-90, 90].</param>
    /// <param name="lon">Longitude in [-180, 180].</param>
    /// <param name="query">Optional human-readable label for <c>?q=…</c>.</param>
    /// <exception cref="ArgumentException">Lat or lon out of range.</exception>
    public static string Build(double lat, double lon, string? query)
    {
        if (lat < -90 || lat > 90)
        {
            // Round-2 review CR-I1: single-arg ArgumentException — see TelPayload for rationale.
            // Format the value via InvariantCulture so e.g. de-DE doesn't render '91,5' for 91.5.
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"--lat must be in [-90, 90]; got {lat}."));
        }
        if (lon < -180 || lon > 180)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"--lon must be in [-180, 180]; got {lon}."));
        }

        string uri = string.Create(CultureInfo.InvariantCulture, $"geo:{lat},{lon}");
        if (!string.IsNullOrEmpty(query))
        {
            uri += $"?q={Uri.EscapeDataString(query)}";
        }
        return uri;
    }
}
