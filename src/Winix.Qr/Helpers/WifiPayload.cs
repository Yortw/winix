#nullable enable
using System;
using System.Text;

namespace Winix.Qr.Helpers;

/// <summary>
/// Builds a ZXing-style Wi-Fi URI payload: <c>WIFI:T:&lt;security&gt;;S:&lt;ssid&gt;;P:&lt;password&gt;;H:true;;</c>.
/// </summary>
public static class WifiPayload
{
    /// <summary>Build the Wi-Fi URI.</summary>
    /// <param name="ssid">Network SSID. Required, non-empty.</param>
    /// <param name="password">Network password. Required unless <paramref name="security"/> is <c>nopass</c>.</param>
    /// <param name="security">One of <c>wpa2</c>, <c>wpa</c>, <c>wep</c>, <c>nopass</c>. Case-insensitive.</param>
    /// <param name="hidden">Whether to mark the network as hidden.</param>
    /// <exception cref="ArgumentException">SSID empty, unknown security type, or missing password when required.</exception>
    public static string Build(string ssid, string? password, string security, bool hidden)
    {
        if (string.IsNullOrEmpty(ssid))
        {
            throw new ArgumentException("SSID must be non-empty.", nameof(ssid));
        }

        string securityCode = security.ToLowerInvariant() switch
        {
            "wpa2" or "wpa" => "WPA",
            "wep" => "WEP",
            "nopass" => "nopass",
            _ => throw new ArgumentException(
                $"Unknown security type '{security}'. Expected wpa2, wpa, wep, or nopass.", nameof(security)),
        };

        bool isOpen = securityCode == "nopass";
        if (!isOpen && string.IsNullOrEmpty(password))
        {
            throw new ArgumentException(
                $"Password is required for security type '{security}'. Use --security nopass for open networks.",
                nameof(password));
        }

        StringBuilder sb = new();
        sb.Append("WIFI:T:");
        sb.Append(securityCode);
        sb.Append(";S:");
        sb.Append(Escape(ssid));
        if (!isOpen)
        {
            sb.Append(";P:");
            sb.Append(Escape(password!));
        }
        if (hidden)
        {
            sb.Append(";H:true");
        }
        sb.Append(";;");
        return sb.ToString();
    }

    private static string Escape(string s)
    {
        StringBuilder sb = new(s.Length);
        foreach (char c in s)
        {
            if (c is '\\' or ';' or ',' or ':' or '"')
            {
                sb.Append('\\');
            }
            sb.Append(c);
        }
        return sb.ToString();
    }
}
