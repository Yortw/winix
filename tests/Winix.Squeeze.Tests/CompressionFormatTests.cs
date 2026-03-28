using Xunit;
using Winix.Squeeze;

namespace Winix.Squeeze.Tests;

public class CompressionFormatMetadataTests
{
    [Theory]
    [InlineData(CompressionFormat.Gzip, ".gz", 6, 1, 9)]
    [InlineData(CompressionFormat.Brotli, ".br", 6, 0, 11)]
    [InlineData(CompressionFormat.Zstd, ".zst", 3, 1, 22)]
    public void GetMetadata_ReturnsCorrectValues(
        CompressionFormat format, string extension, int defaultLevel, int minLevel, int maxLevel)
    {
        var meta = CompressionFormatInfo.GetMetadata(format);

        Assert.Equal(extension, meta.Extension);
        Assert.Equal(defaultLevel, meta.DefaultLevel);
        Assert.Equal(minLevel, meta.MinLevel);
        Assert.Equal(maxLevel, meta.MaxLevel);
    }

    [Theory]
    [InlineData(CompressionFormat.Gzip, "gz")]
    [InlineData(CompressionFormat.Brotli, "br")]
    [InlineData(CompressionFormat.Zstd, "zst")]
    public void GetShortName_ReturnsExpected(CompressionFormat format, string expected)
    {
        Assert.Equal(expected, CompressionFormatInfo.GetShortName(format));
    }

    [Theory]
    [InlineData(CompressionFormat.Gzip, new byte[] { 0x1f, 0x8b })]
    [InlineData(CompressionFormat.Zstd, new byte[] { 0x28, 0xb5, 0x2f, 0xfd })]
    public void GetMagicBytes_ReturnsCorrectBytes(CompressionFormat format, byte[] expected)
    {
        var magic = CompressionFormatInfo.GetMagicBytes(format);

        Assert.NotNull(magic);
        Assert.Equal(expected, magic);
    }

    [Fact]
    public void GetMagicBytes_Brotli_ReturnsNull()
    {
        var magic = CompressionFormatInfo.GetMagicBytes(CompressionFormat.Brotli);

        Assert.Null(magic);
    }

    [Theory]
    [InlineData(CompressionFormat.Gzip, 1, true)]
    [InlineData(CompressionFormat.Gzip, 9, true)]
    [InlineData(CompressionFormat.Gzip, 0, false)]
    [InlineData(CompressionFormat.Gzip, 10, false)]
    [InlineData(CompressionFormat.Brotli, 0, true)]
    [InlineData(CompressionFormat.Brotli, 11, true)]
    [InlineData(CompressionFormat.Brotli, 12, false)]
    [InlineData(CompressionFormat.Zstd, 1, true)]
    [InlineData(CompressionFormat.Zstd, 22, true)]
    [InlineData(CompressionFormat.Zstd, 0, false)]
    [InlineData(CompressionFormat.Zstd, 23, false)]
    public void IsLevelValid_ReturnsExpected(CompressionFormat format, int level, bool expected)
    {
        Assert.Equal(expected, CompressionFormatInfo.IsLevelValid(format, level));
    }
}
