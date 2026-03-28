using Xunit;
using Winix.Squeeze;

namespace Winix.Squeeze.Tests;

public class CompressTests
{
    private static readonly byte[] TestData = "The quick brown fox jumps over the lazy dog. "u8.ToArray();

    private static byte[] GenerateTestData(int size)
    {
        byte[] data = new byte[size];
        for (int i = 0; i < size; i++)
        {
            data[i] = TestData[i % TestData.Length];
        }
        return data;
    }

    [Theory]
    [InlineData(CompressionFormat.Gzip, 1)]
    [InlineData(CompressionFormat.Gzip, 6)]
    [InlineData(CompressionFormat.Gzip, 9)]
    [InlineData(CompressionFormat.Brotli, 0)]
    [InlineData(CompressionFormat.Brotli, 6)]
    [InlineData(CompressionFormat.Brotli, 11)]
    [InlineData(CompressionFormat.Zstd, 1)]
    [InlineData(CompressionFormat.Zstd, 3)]
    [InlineData(CompressionFormat.Zstd, 22)]
    public async Task Compress_ThenDecompress_RoundTrips(CompressionFormat format, int level)
    {
        byte[] original = GenerateTestData(10_000);
        using var input = new MemoryStream(original);
        using var compressed = new MemoryStream();

        await Compressor.CompressAsync(input, compressed, format, level);

        Assert.True(compressed.Length > 0);
        Assert.True(compressed.Length < original.Length);

        compressed.Position = 0;
        using var decompressed = new MemoryStream();
        await Compressor.DecompressAsync(compressed, decompressed, format);

        Assert.Equal(original, decompressed.ToArray());
    }

    [Fact]
    public async Task Compress_EmptyInput_ProducesValidOutput()
    {
        using var input = new MemoryStream(Array.Empty<byte>());
        using var compressed = new MemoryStream();

        await Compressor.CompressAsync(input, compressed, CompressionFormat.Gzip, 6);
        Assert.True(compressed.Length > 0);

        compressed.Position = 0;
        using var decompressed = new MemoryStream();
        await Compressor.DecompressAsync(compressed, decompressed, CompressionFormat.Gzip);
        Assert.Empty(decompressed.ToArray());
    }

    [Theory]
    [InlineData(CompressionFormat.Gzip)]
    [InlineData(CompressionFormat.Brotli)]
    [InlineData(CompressionFormat.Zstd)]
    public async Task Compress_DefaultLevel_Works(CompressionFormat format)
    {
        byte[] original = GenerateTestData(5_000);
        int defaultLevel = CompressionFormatInfo.GetDefaultLevel(format);

        using var input = new MemoryStream(original);
        using var compressed = new MemoryStream();
        await Compressor.CompressAsync(input, compressed, format, defaultLevel);

        compressed.Position = 0;
        using var decompressed = new MemoryStream();
        await Compressor.DecompressAsync(compressed, decompressed, format);
        Assert.Equal(original, decompressed.ToArray());
    }
}
