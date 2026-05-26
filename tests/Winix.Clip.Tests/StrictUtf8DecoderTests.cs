#nullable enable

using Xunit;
using Winix.Clip;

namespace Winix.Clip.Tests;

/// <summary>
/// F1 regression pins — see artifacts/reverify-2026-05-06/clip/BASELINE-REPORT.md
/// for the full table of BOM-prefixed input scenarios. Each test below corresponds
/// to one row of that table and would have failed pre-fix (when the StreamReader
/// path silently switched encoders on BOM-prefixed input, then decoded under a
/// replacement-fallback that produced U+FFFD or stripped the BOM).
/// </summary>
public class StrictUtf8DecoderTests
{
    [Fact]
    public void TryDecode_EmptyInput_ReturnsTrueAndEmptyString()
    {
        bool ok = StrictUtf8Decoder.TryDecode(System.Array.Empty<byte>(), out string content);
        Assert.True(ok);
        Assert.Equal(string.Empty, content);
    }

    [Fact]
    public void TryDecode_PlainAscii_ReturnsTrueAndExactString()
    {
        // 'hello' — uncontroversial UTF-8 happy path.
        byte[] bytes = { 0x68, 0x65, 0x6C, 0x6C, 0x6F };
        bool ok = StrictUtf8Decoder.TryDecode(bytes, out string content);
        Assert.True(ok);
        Assert.Equal("hello", content);
    }

    [Fact]
    public void TryDecode_Utf16LeBomWithInvalidTrailer_ReturnsFalse()
    {
        // 0xFF 0xFE 0xFD — UTF-16 LE BOM bytes plus an invalid UTF-8 trailer.
        // None of these three bytes form a valid UTF-8 start byte, so strict decode
        // must reject. Pre-fix StreamReader would have switched to UTF-16 LE on the
        // BOM and tried to decode the 0xFD as a (single-byte, malformed) UTF-16 char,
        // producing U+FFFD via replacement fallback and exiting 0.
        byte[] bytes = { 0xFF, 0xFE, 0xFD };
        bool ok = StrictUtf8Decoder.TryDecode(bytes, out string content);
        Assert.False(ok);
        Assert.Equal(string.Empty, content);
    }

    [Fact]
    public void TryDecode_Utf16LeBomWithValidLatinAfter_ReturnsFalse()
    {
        // 0xFF 0xFE 0x41 0x00 — UTF-16 LE BOM + 'A' encoded as little-endian UTF-16.
        // Pre-fix StreamReader detected the BOM, switched to UTF-16 LE, decoded 'A'
        // and exited 0 with 'A' on the clipboard. Strict UTF-8 must reject because
        // 0xFF is not a valid UTF-8 start byte.
        byte[] bytes = { 0xFF, 0xFE, 0x41, 0x00 };
        bool ok = StrictUtf8Decoder.TryDecode(bytes, out string content);
        Assert.False(ok);
        Assert.Equal(string.Empty, content);
    }

    [Fact]
    public void TryDecode_Utf16BeBomWithValidLatinAfter_ReturnsFalse()
    {
        // 0xFE 0xFF 0x00 0x41 — UTF-16 BE BOM + 'A' encoded as big-endian UTF-16.
        // Symmetric to the LE case. Pre-fix would have decoded 'A' under UTF-16 BE.
        byte[] bytes = { 0xFE, 0xFF, 0x00, 0x41 };
        bool ok = StrictUtf8Decoder.TryDecode(bytes, out string content);
        Assert.False(ok);
        Assert.Equal(string.Empty, content);
    }

    [Fact]
    public void TryDecode_Utf8BomFollowedByContent_ReturnsTrueWithBomPreserved()
    {
        // 0xEF 0xBB 0xBF 'hello' — the UTF-8 BOM (which is itself valid UTF-8 and
        // encodes U+FEFF) followed by 'hello'. README §"copy is byte-preserving"
        // promises the BOM passes through. Pre-fix StreamReader silently STRIPPED
        // the BOM (consumed it as an encoding hint), violating the contract.
        // Post-fix: BOM is just three valid UTF-8 bytes encoding U+FEFF and is
        // preserved as the first character of the decoded string.
        byte[] bytes = { 0xEF, 0xBB, 0xBF, 0x68, 0x65, 0x6C, 0x6C, 0x6F };
        bool ok = StrictUtf8Decoder.TryDecode(bytes, out string content);
        Assert.True(ok);
        Assert.Equal("﻿hello", content);
    }

    [Fact]
    public void TryDecode_MidStreamInvalidByteNoBom_ReturnsFalse()
    {
        // 'valid' + 0xFF — invalid UTF-8 byte mid-stream, no BOM. This case has
        // ALWAYS been correctly rejected (the strict UTF8Encoding throws on the
        // 0xFF regardless of detectEncodingFromByteOrderMarks). Pin so a future
        // refactor that loosens the encoder to replacement-fallback would be caught.
        byte[] bytes = { 0x76, 0x61, 0x6C, 0x69, 0x64, 0xFF };
        bool ok = StrictUtf8Decoder.TryDecode(bytes, out string content);
        Assert.False(ok);
        Assert.Equal(string.Empty, content);
    }
}
