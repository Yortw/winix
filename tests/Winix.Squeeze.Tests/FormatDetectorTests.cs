using Xunit;
using Winix.Squeeze;

namespace Winix.Squeeze.Tests;

public class DetectFromMagicBytesTests
{
    [Fact]
    public void DetectFromMagicBytes_GzipHeader_ReturnsGzip()
    {
        byte[] header = { 0x1f, 0x8b, 0x08, 0x00 };
        Assert.Equal(CompressionFormat.Gzip, FormatDetector.DetectFromMagicBytes(header));
    }

    [Fact]
    public void DetectFromMagicBytes_ZstdHeader_ReturnsZstd()
    {
        byte[] header = { 0x28, 0xb5, 0x2f, 0xfd, 0x00 };
        Assert.Equal(CompressionFormat.Zstd, FormatDetector.DetectFromMagicBytes(header));
    }

    [Fact]
    public void DetectFromMagicBytes_UnknownHeader_ReturnsNull()
    {
        byte[] header = { 0x50, 0x4b, 0x03, 0x04 }; // ZIP magic
        Assert.Null(FormatDetector.DetectFromMagicBytes(header));
    }

    [Fact]
    public void DetectFromMagicBytes_TooShort_ReturnsNull()
    {
        byte[] header = { 0x1f };
        Assert.Null(FormatDetector.DetectFromMagicBytes(header));
    }

    [Fact]
    public void DetectFromMagicBytes_Empty_ReturnsNull()
    {
        Assert.Null(FormatDetector.DetectFromMagicBytes(Array.Empty<byte>()));
    }
}

public class DetectFromExtensionTests
{
    [Theory]
    [InlineData("file.gz", CompressionFormat.Gzip)]
    [InlineData("file.br", CompressionFormat.Brotli)]
    [InlineData("file.zst", CompressionFormat.Zstd)]
    [InlineData("FILE.GZ", CompressionFormat.Gzip)]
    [InlineData("archive.tar.gz", CompressionFormat.Gzip)]
    public void DetectFromExtension_KnownExtension_ReturnsFormat(
        string filename, CompressionFormat expected)
    {
        Assert.Equal(expected, FormatDetector.DetectFromExtension(filename));
    }

    [Theory]
    [InlineData("file.txt")]
    [InlineData("file.zip")]
    [InlineData("file")]
    [InlineData("")]
    public void DetectFromExtension_UnknownExtension_ReturnsNull(string filename)
    {
        Assert.Null(FormatDetector.DetectFromExtension(filename));
    }
}

public class DetectFromStreamTests
{
    [Fact]
    public async Task DetectFromStream_GzipData_ReturnsGzip()
    {
        using var ms = new MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Compress, leaveOpen: true))
        {
            gz.Write("hello"u8);
        }
        ms.Position = 0;

        var (format, headerBytes) = await FormatDetector.DetectFromStreamAsync(ms, filename: null);

        Assert.Equal(CompressionFormat.Gzip, format);
        Assert.NotNull(headerBytes);
    }

    [Fact]
    public async Task DetectFromStream_ZstdData_ReturnsZstd()
    {
        using var ms = new MemoryStream();
        using (var compressor = new ZstdSharp.Compressor(3))
        {
            byte[] input = "hello zstd test data"u8.ToArray();
            byte[] compressed = compressor.Wrap(input).ToArray();
            ms.Write(compressed);
        }
        ms.Position = 0;

        var (format, headerBytes) = await FormatDetector.DetectFromStreamAsync(ms, filename: null);

        Assert.Equal(CompressionFormat.Zstd, format);
        Assert.NotNull(headerBytes);
    }

    [Fact]
    public async Task DetectFromStream_BrotliWithExtension_ReturnsBrotli()
    {
        using var ms = new MemoryStream();
        using (var br = new System.IO.Compression.BrotliStream(ms, System.IO.Compression.CompressionMode.Compress, leaveOpen: true))
        {
            br.Write("hello brotli"u8);
        }
        ms.Position = 0;

        var (format, headerBytes) = await FormatDetector.DetectFromStreamAsync(ms, filename: "file.br");

        Assert.Equal(CompressionFormat.Brotli, format);
    }
}
