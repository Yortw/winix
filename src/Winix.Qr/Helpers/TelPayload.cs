#nullable enable
using System;

namespace Winix.Qr.Helpers;

/// <summary>Builds a <c>tel:</c> URI per RFC 3966.</summary>
public static class TelPayload
{
    /// <summary>Build the tel URI.</summary>
    /// <param name="number">Phone number. Whitespace is stripped; the remaining characters must be
    /// from the RFC 3966 visible-element set (digits, optional leading <c>+</c>, separators
    /// <c>(</c> <c>)</c> <c>-</c> <c>.</c> <c>/</c>, and the <c>;ext=</c> / <c>;phone-context=</c>
    /// parameters per RFC 3966 §3). Disallowed characters cause an <see cref="ArgumentException"/>.</param>
    /// <exception cref="ArgumentException">Number empty after trimming, or contains characters
    /// disallowed by RFC 3966.</exception>
    /// <remarks>
    /// Round-1 review SFH-I3: previously the tool accepted any string and produced a tel: URI with
    /// the literal junk (e.g. <c>"+1 (555) abc;DROP"</c>), which scanning apps reject silently. This
    /// sanitisation refuses obviously-broken numbers at parse time so the user sees the failure
    /// before generating the QR.
    /// </remarks>
    public static string Build(string number)
    {
        if (string.IsNullOrEmpty(number))
        {
            throw new ArgumentException("Number must be non-empty.");
        }
        string sanitised = SanitisePhoneNumber(number);
        return $"tel:{sanitised}";
    }

    /// <summary>
    /// Sanitise a user-supplied phone number per RFC 3966's visible-element grammar.
    /// Strips ASCII whitespace, then validates each remaining character against the allowed set
    /// (digits, optional leading '+', separators <c>()-./*#</c>, plus optional <c>;param=value</c>
    /// extensions where param values use alnum + <c>=.-_</c>).
    /// </summary>
    /// <exception cref="ArgumentException">Number is empty after stripping whitespace,
    /// contains no digits, or contains disallowed characters.</exception>
    /// <remarks>
    /// Round-1 review SFH-I3: shared between <see cref="TelPayload.Build"/> and
    /// <see cref="SmsPayload.Build"/>. Internal — exposed via InternalsVisibleTo for tests.
    ///
    /// Note: throws <see cref="ArgumentException"/> WITHOUT a paramName argument. The two-arg
    /// constructor's auto-suffix (" (Parameter 'name')") relies on a localisable resource that
    /// renders as "Arg_ParamName_Name" under InvariantGlobalization=true (which qr.csproj sets).
    /// The caller surface includes enough context in the message itself.
    /// </remarks>
    internal static string SanitisePhoneNumber(string raw)
    {
        // 1. Strip ASCII whitespace — common copy/paste artefact, not part of any URI scheme.
        Span<char> buf = raw.Length <= 256 ? stackalloc char[raw.Length] : new char[raw.Length];
        int len = 0;
        foreach (char c in raw)
        {
            if (c is ' ' or '\t' or '\r' or '\n') continue;
            buf[len++] = c;
        }
        if (len == 0)
        {
            throw new ArgumentException("Number must contain at least one digit.");
        }

        ReadOnlySpan<char> stripped = buf[..len];

        // 2. Split off optional ';' parameters (RFC 3966 ext, phone-context, etc.).
        int semi = stripped.IndexOf(';');
        ReadOnlySpan<char> body = semi < 0 ? stripped : stripped[..semi];
        ReadOnlySpan<char> @params = semi < 0 ? default : stripped[(semi + 1)..];

        // 3. Validate body: optional leading '+', then digits / separators / '*' / '#'.
        bool hasDigit = false;
        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            if (i == 0 && c == '+') continue;
            if (c >= '0' && c <= '9') { hasDigit = true; continue; }
            if (c is '(' or ')' or '-' or '.' or '/' or '*' or '#') continue;
            throw new ArgumentException(
                $"Phone number '{raw}' contains disallowed character '{c}'. " +
                "Allowed: digits, optional leading '+', and separators ()-./*#.");
        }
        if (!hasDigit)
        {
            throw new ArgumentException("Number must contain at least one digit.");
        }

        // 4. Validate params (light): allow alnum, '=', '.', '-', '_', ';' between params.
        for (int i = 0; i < @params.Length; i++)
        {
            char c = @params[i];
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) continue;
            if (c is '=' or '.' or '-' or '_' or ';' or '+') continue;
            throw new ArgumentException(
                $"Phone number parameter contains disallowed character '{c}'.");
        }

        return new string(stripped);
    }
}
