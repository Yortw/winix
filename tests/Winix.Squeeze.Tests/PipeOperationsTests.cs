using Xunit;
using Winix.Squeeze;

namespace Winix.Squeeze.Tests;

public class PipeOperationsTests : IDisposable
{
    private readonly string _tempDir;

    public PipeOperationsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"squeeze-pipe-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task ProcessAsync_CompressAndDecompress_RoundTrips()
    {
        byte[] original = "Hello, pipe operations!"u8.ToArray();

        // Compress
        using var compressInput = new MemoryStream(original);
        using var compressed = new MemoryStream();
        var compressResult = await PipeOperations.ProcessAsync(
            compressInput, compressed,
            decompress: false, CompressionFormat.Gzip, level: 6, explicitFormat: null);

        Assert.Equal(0, compressResult.ExitCode);
        Assert.NotNull(compressResult.Result);
        Assert.True(compressed.Length > 0);

        // Decompress
        compressed.Position = 0;
        using var decompressed = new MemoryStream();
        var decompressResult = await PipeOperations.ProcessAsync(
            compressed, decompressed,
            decompress: true, CompressionFormat.Gzip, level: 6, explicitFormat: null);

        Assert.Equal(0, decompressResult.ExitCode);
        Assert.Equal(original, decompressed.ToArray());
    }

    [Fact]
    public async Task ProcessAsync_DecompressAutoDetect_DetectsFormat()
    {
        byte[] original = "Auto-detect test"u8.ToArray();

        using var compressInput = new MemoryStream(original);
        using var compressed = new MemoryStream();
        await PipeOperations.ProcessAsync(
            compressInput, compressed,
            decompress: false, CompressionFormat.Gzip, level: 6, explicitFormat: null);

        compressed.Position = 0;
        using var decompressed = new MemoryStream();
        var result = await PipeOperations.ProcessAsync(
            compressed, decompressed,
            decompress: true, CompressionFormat.Gzip, level: 6, explicitFormat: null);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(original, decompressed.ToArray());
    }

    [Fact]
    public async Task ProcessAsync_DecompressInvalidData_ReturnsError()
    {
        byte[] garbage = "not compressed data"u8.ToArray();

        using var input = new MemoryStream(garbage);
        using var output = new MemoryStream();
        var result = await PipeOperations.ProcessAsync(
            input, output,
            decompress: true, CompressionFormat.Gzip, level: 6, explicitFormat: CompressionFormat.Gzip);

        Assert.Equal(1, result.ExitCode);
        Assert.NotNull(result.ErrorMessage);
    }
}
