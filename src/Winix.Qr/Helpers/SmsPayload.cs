#nullable enable
using System;

namespace Winix.Qr.Helpers;

/// <summary>Builds an <c>sms:</c> URI per RFC 5724.</summary>
public static class SmsPayload
{
    /// <summary>Build the SMS URI.</summary>
    /// <param name="number">Phone number (E.164 recommended). Required, non-empty.</param>
    /// <param name="message">Optional pre-composed message body.</param>
    /// <exception cref="ArgumentException">Number empty.</exception>
    public static string Build(string number, string? message)
    {
        if (string.IsNullOrEmpty(number))
        {
            throw new ArgumentException("Number must be non-empty.", nameof(number));
        }

        string uri = $"sms:{number}";
        if (!string.IsNullOrEmpty(message))
        {
            uri += $"?body={Uri.EscapeDataString(message)}";
        }
        return uri;
    }
}
