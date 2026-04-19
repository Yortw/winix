#nullable enable
using System;
using Winix.Codec;
using Yort.ShellKit;

namespace Winix.Digest;

/// <summary>Pure functions composing digest's output lines and JSON elements.</summary>
public static class Formatting
{
    /// <summary>Encodes hash bytes as a string according to the requested output format.</summary>
    public static string Encode(byte[] hash, OutputFormat format, bool uppercase)
    {
        return format switch
        {
            OutputFormat.Hex       => Hex.Encode(hash, uppercase),
            OutputFormat.Base64    => Base64.Encode(hash, urlSafe: false),
            OutputFormat.Base64Url => Base64.Encode(hash, urlSafe: true),
            OutputFormat.Base32    => Base32Crockford.Encode(hash),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };
    }

    /// <summary>Single-input plain text: just the encoded hash (no trailing newline; caller adds it).</summary>
    public static string PlainSingle(byte[] hash, OutputFormat format, bool uppercase)
    {
        return Encode(hash, format, uppercase);
    }

    /// <summary>
    /// Multi-file plain text: sha256sum-compatible <c>&lt;hash&gt; *&lt;filename&gt;</c> with the
    /// binary-mode marker. <c>*</c> signals that the hash was computed over raw bytes
    /// (no CR/LF translation) — honest to digest's behaviour and compatible with
    /// <c>sha256sum -c</c> verification flows.
    /// </summary>
    public static string PlainMultiLine(byte[] hash, string filename, OutputFormat format, bool uppercase)
    {
        return $"{Encode(hash, format, uppercase)} *{filename}";
    }

    /// <summary>
    /// JSON element for one hash result. When multiple results are emitted (multi-file
    /// mode), the caller wraps these in a JSON array.
    /// </summary>
    public static string JsonElement(byte[] hash, string? path, DigestOptions options)
    {
        var (writer, buffer) = JsonHelper.CreateWriter();
        using (writer)
        {
            writer.WriteStartObject();
            writer.WriteString("algorithm", FormatAlgorithmName(options));
            writer.WriteString("format", FormatName(options.Format));
            writer.WriteString("hash", Encode(hash, options.Format, options.Uppercase));
            writer.WriteString("source", path is null ? InferSource(options) : "file");
            if (path is not null)
            {
                writer.WriteString("path", path);
            }
            writer.WriteEndObject();
        }
        return JsonHelper.GetString(buffer);
    }

    private static string FormatAlgorithmName(DigestOptions options)
    {
        string algo = options.Algorithm switch
        {
            HashAlgorithm.Sha256   => "sha256",
            HashAlgorithm.Sha384   => "sha384",
            HashAlgorithm.Sha512   => "sha512",
            HashAlgorithm.Sha1     => "sha1",
            HashAlgorithm.Md5      => "md5",
            HashAlgorithm.Sha3_256 => "sha3-256",
            HashAlgorithm.Sha3_512 => "sha3-512",
            HashAlgorithm.Blake2b  => "blake2b",
            _ => "unknown",
        };
        return options.IsHmac ? $"hmac-{algo}" : algo;
    }

    private static string FormatName(OutputFormat format) => format switch
    {
        OutputFormat.Hex       => "hex",
        OutputFormat.Base64    => "base64",
        OutputFormat.Base64Url => "base64url",
        OutputFormat.Base32    => "base32",
        _ => "unknown",
    };

    private static string InferSource(DigestOptions options) => options.Source switch
    {
        StringInput     => "string",
        StdinInput      => "stdin",
        SingleFileInput => "file",
        MultiFileInput  => "file",
        _ => "unknown",
    };
}
