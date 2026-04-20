#nullable enable
using System;

namespace Winix.Qr.Helpers;

/// <summary>Builds a <c>tel:</c> URI per RFC 3966.</summary>
public static class TelPayload
{
    /// <summary>Build the tel URI.</summary>
    /// <exception cref="ArgumentException">Number empty.</exception>
    public static string Build(string number)
    {
        if (string.IsNullOrEmpty(number))
        {
            throw new ArgumentException("Number must be non-empty.", nameof(number));
        }
        return $"tel:{number}";
    }
}
