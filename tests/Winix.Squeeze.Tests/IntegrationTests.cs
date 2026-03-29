using Xunit;
using Winix.Squeeze;

namespace Winix.Squeeze.Tests;

public class IntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private const string TestContent = "This is test content for integration testing. It repeats. " +
        "This is test content for integration testing. It repeats. " +
        "This is test content for integration testing. It repeats. " +
        "This is test content for integration testing. It repeats. ";

    public IntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "squeeze-integ-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Theory]
    [InlineData(CompressionFormat.Gzip, ".gz")]
    [InlineData(CompressionFormat.Brotli, ".br")]
    [InlineData(CompressionFormat.Zstd, ".zst")]
    public async Task FullRoundTrip_AllFormats(CompressionFormat format, string extension)
    {
        string inputPath = Path.Combine(_tempDir, $"test{extension}.txt");
        await File.WriteAllTextAsync(inputPath, TestContent);

        // Compress
        int level = CompressionFormatInfo.GetDefaultLevel(format);
        var compressResult = await FileOperations.CompressFileAsync(
            inputPath, null, format, level, force: false, remove: false);

        Assert.Equal(0, compressResult.ExitCode);
        Assert.NotNull(compressResult.Result);
        Assert.Equal(inputPath + extension, compressResult.Result!.OutputPath);
        Assert.True(File.Exists(compressResult.Result.OutputPath));

        // Input preserved
        Assert.True(File.Exists(inputPath));

        // Compressed file should be smaller
        long compressedSize = new FileInfo(compressResult.Result.OutputPath).Length;
        long originalSize = new FileInfo(inputPath).Length;
        Assert.True(compressedSize < originalSize,
            $"Compressed ({compressedSize}) should be smaller than original ({originalSize})");

        // Delete original so decompress output path is free
        File.Delete(inputPath);

        // Decompress
        var decompressResult = await FileOperations.DecompressFileAsync(
            compressResult.Result.OutputPath, null, explicitFormat: null,
            force: false, remove: false);

        Assert.Equal(0, decompressResult.ExitCode);
        Assert.True(File.Exists(inputPath));
        Assert.Equal(TestContent, await File.ReadAllTextAsync(inputPath));
    }

    [Fact]
    public async Task MultiFile_EachGetsOwnOutput()
    {
        string[] filenames = { "one.txt", "two.txt", "three.txt" };
        foreach (string name in filenames)
        {
            await File.WriteAllTextAsync(Path.Combine(_tempDir, name), TestContent + name);
        }

        foreach (string name in filenames)
        {
            string path = Path.Combine(_tempDir, name);
            var result = await FileOperations.CompressFileAsync(
                path, null, CompressionFormat.Gzip, 6, force: false, remove: false);

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(path + ".gz"));
        }
    }

    [Fact]
    public async Task Remove_DeletesInputAfterSuccess()
    {
        string inputPath = Path.Combine(_tempDir, "remove-me.txt");
        await File.WriteAllTextAsync(inputPath, TestContent);

        var result = await FileOperations.CompressFileAsync(
            inputPath, null, CompressionFormat.Gzip, 6, force: false, remove: true);

        Assert.Equal(0, result.ExitCode);
        Assert.False(File.Exists(inputPath), "Input should be deleted when --remove is used");
        Assert.True(File.Exists(inputPath + ".gz"), "Output should exist");
    }

    [Fact]
    public async Task OverwriteProtection_BlocksWithoutForce()
    {
        string inputPath = Path.Combine(_tempDir, "protect.txt");
        string outputPath = inputPath + ".gz";
        await File.WriteAllTextAsync(inputPath, TestContent);
        await File.WriteAllTextAsync(outputPath, "existing");

        var result = await FileOperations.CompressFileAsync(
            inputPath, null, CompressionFormat.Gzip, 6, force: false, remove: false);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("output_exists", result.ExitReason);
    }

    [Fact]
    public async Task OverwriteProtection_AllowsWithForce()
    {
        string inputPath = Path.Combine(_tempDir, "force.txt");
        string outputPath = inputPath + ".gz";
        await File.WriteAllTextAsync(inputPath, TestContent);
        await File.WriteAllTextAsync(outputPath, "existing");

        var result = await FileOperations.CompressFileAsync(
            inputPath, null, CompressionFormat.Gzip, 6, force: true, remove: false);

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task InputNotFound_ReturnsFileNotFound()
    {
        var result = await FileOperations.CompressFileAsync(
            Path.Combine(_tempDir, "ghost.txt"), null,
            CompressionFormat.Gzip, 6, force: false, remove: false);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("file_not_found", result.ExitReason);
    }

    [Fact]
    public async Task StreamRoundTrip_Gzip()
    {
        byte[] original = System.Text.Encoding.UTF8.GetBytes(TestContent);

        using var compressed = new MemoryStream();
        await Compressor.CompressAsync(new MemoryStream(original), compressed, CompressionFormat.Gzip, 6);

        compressed.Position = 0;
        using var decompressed = new MemoryStream();
        await Compressor.DecompressAsync(compressed, decompressed, CompressionFormat.Gzip);

        Assert.Equal(original, decompressed.ToArray());
    }

    [Fact]
    public async Task StreamRoundTrip_Brotli()
    {
        byte[] original = System.Text.Encoding.UTF8.GetBytes(TestContent);

        using var compressed = new MemoryStream();
        await Compressor.CompressAsync(new MemoryStream(original), compressed, CompressionFormat.Brotli, 6);

        compressed.Position = 0;
        using var decompressed = new MemoryStream();
        await Compressor.DecompressAsync(compressed, decompressed, CompressionFormat.Brotli);

        Assert.Equal(original, decompressed.ToArray());
    }

    [Fact]
    public async Task StreamRoundTrip_Zstd()
    {
        byte[] original = System.Text.Encoding.UTF8.GetBytes(TestContent);

        using var compressed = new MemoryStream();
        await Compressor.CompressAsync(new MemoryStream(original), compressed, CompressionFormat.Zstd, 3);

        compressed.Position = 0;
        using var decompressed = new MemoryStream();
        await Compressor.DecompressAsync(compressed, decompressed, CompressionFormat.Zstd);

        Assert.Equal(original, decompressed.ToArray());
    }

    [Fact]
    public async Task DecompressAutoDetect_GzipFile_DetectedByMagicBytes()
    {
        string inputPath = Path.Combine(_tempDir, "detect.txt");
        string compressedPath = inputPath + ".gz";
        await File.WriteAllTextAsync(inputPath, TestContent);

        await FileOperations.CompressFileAsync(
            inputPath, null, CompressionFormat.Gzip, 6, force: false, remove: false);
        File.Delete(inputPath);

        var result = await FileOperations.DecompressFileAsync(
            compressedPath, null, explicitFormat: null, force: false, remove: false);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(TestContent, await File.ReadAllTextAsync(inputPath));
    }

    [Fact]
    public async Task ExplicitOutput_CustomFilename()
    {
        string inputPath = Path.Combine(_tempDir, "custom.txt");
        string outputPath = Path.Combine(_tempDir, "custom.compressed");
        await File.WriteAllTextAsync(inputPath, TestContent);

        var result = await FileOperations.CompressFileAsync(
            inputPath, outputPath, CompressionFormat.Gzip, 6, force: false, remove: false);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputPath));
        Assert.Equal(outputPath, result.Result!.OutputPath);
    }
}
