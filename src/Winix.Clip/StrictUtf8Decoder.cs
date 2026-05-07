#nullable enable

using System.Text;

namespace Winix.Clip;

/// <summary>
/// Strict UTF-8 byte-array decoder. Rejects any input that is not well-formed UTF-8,
/// including byte sequences that begin with a UTF-16 LE BOM (<c>0xFF 0xFE</c>) or
/// UTF-16 BE BOM (<c>0xFE 0xFF</c>) — those byte patterns are not valid UTF-8 start
/// bytes and must be rejected, not interpreted as a different encoding.
/// </summary>
/// <remarks>
/// <para>
/// This helper exists specifically to close the F1 (BOM-bypass) defect class. The
/// previous implementation used <see cref="StreamReader"/> with a strict UTF-8
/// <see cref="UTF8Encoding"/> argument, but <c>StreamReader</c>'s default
/// <c>detectEncodingFromByteOrderMarks: true</c> caused it to switch to a different
/// (replacement-fallback) decoder when the input began with any of the three BOMs,
/// silently bypassing strict validation. The <see cref="UTF8Encoding"/>'s
/// <c>throwOnInvalidBytes</c> guard was rendered ineffective for those inputs.
/// </para>
/// <para>
/// This helper instead calls <see cref="Encoding.GetString(byte[])"/> on a strict
/// <see cref="UTF8Encoding"/> directly, so the <see cref="DecoderFallbackException"/>
/// fires for every invalid sequence. UTF-8 BOM bytes (<c>0xEF 0xBB 0xBF</c>) are
/// themselves valid UTF-8 (they encode U+FEFF) and pass through preserved — the
/// caller decides whether to strip the U+FEFF code point or keep it byte-for-byte.
/// </para>
/// </remarks>
public static class StrictUtf8Decoder
{
    /// <summary>
    /// Attempts to decode <paramref name="bytes"/> as strict UTF-8.
    /// </summary>
    /// <param name="bytes">The input byte array.</param>
    /// <param name="content">
    /// On success: the decoded string. On failure: <see cref="string.Empty"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when every byte forms a valid UTF-8 sequence (including
    /// empty input, which decodes to <see cref="string.Empty"/>);
    /// <see langword="false"/> when any byte sequence is invalid UTF-8.
    /// </returns>
    public static bool TryDecode(byte[] bytes, out string content)
    {
        var encoding = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);
        try
        {
            content = encoding.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            content = string.Empty;
            return false;
        }
    }
}
