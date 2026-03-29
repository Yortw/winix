using Xunit;
using Winix.Squeeze;

namespace Winix.Squeeze.Tests;

public class OutputNamingTests
{
    [Theory]
    [InlineData("data.txt", CompressionFormat.Gzip, "data.txt.gz")]
    [InlineData("data.txt", CompressionFormat.Brotli, "data.txt.br")]
    [InlineData("data.txt", CompressionFormat.Zstd, "data.txt.zst")]
    [InlineData("/tmp/archive.tar", CompressionFormat.Gzip, "/tmp/archive.tar.gz")]
    public void GetCompressOutputPath_AppendsExtension(string input, CompressionFormat format, string expected)
    {
        Assert.Equal(expected, FileOperations.GetCompressOutputPath(input, format));
    }

    [Theory]
    [InlineData("data.txt.gz", "data.txt")]
    [InlineData("data.txt.br", "data.txt")]
    [InlineData("data.txt.zst", "data.txt")]
    [InlineData("/tmp/archive.tar.gz", "/tmp/archive.tar")]
    public void GetDecompressOutputPath_StripsExtension(string input, string expected)
    {
        Assert.Equal(expected, FileOperations.GetDecompressOutputPath(input));
    }

    [Fact]
    public void GetDecompressOutputPath_UnknownExtension_ReturnsNull()
    {
        Assert.Null(FileOperations.GetDecompressOutputPath("data.txt"));
    }

    [Fact]
    public void GetDecompressOutputPath_NoExtension_ReturnsNull()
    {
        Assert.Null(FileOperations.GetDecompressOutputPath("data"));
    }
}

public class FileOperationIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public FileOperationIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "squeeze_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    private string CreateTestFile(string name, int size = 10_000)
    {
        string path = Path.Combine(_tempDir, name);
        byte[] data = new byte[size];
        byte[] pattern = "The quick brown fox jumps over the lazy dog. "u8.ToArray();
        for (int i = 0; i < size; i++)
        {
            data[i] = pattern[i % pattern.Length];
        }
        File.WriteAllBytes(path, data);
        return path;
    }

    [Fact]
    public async Task CompressFile_CreatesOutputAndKeepsInput()
    {
        string input = CreateTestFile("data.txt");

        var result = await FileOperations.CompressFileAsync(
            input, outputPath: null, CompressionFormat.Gzip, level: 6, force: false, remove: false);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("success", result.ExitReason);
        Assert.NotNull(result.Result);
        Assert.True(File.Exists(input), "Input file should still exist");
        Assert.True(File.Exists(result.Result!.OutputPath), "Output file should exist");
        Assert.True(result.Result.OutputBytes > 0);
        Assert.True(result.Result.OutputBytes < result.Result.InputBytes, "Compressed should be smaller");
    }

    [Fact]
    public async Task CompressFile_WithRemove_DeletesInput()
    {
        string input = CreateTestFile("removeme.txt");

        var result = await FileOperations.CompressFileAsync(
            input, outputPath: null, CompressionFormat.Gzip, level: 6, force: false, remove: true);

        Assert.Equal(0, result.ExitCode);
        Assert.False(File.Exists(input), "Input file should be deleted when remove is true");
        Assert.True(File.Exists(result.Result!.OutputPath));
    }

    [Fact]
    public async Task CompressFile_OutputExists_WithoutForce_ReturnsError()
    {
        string input = CreateTestFile("data.txt");
        string outputPath = Path.Combine(_tempDir, "data.txt.gz");
        File.WriteAllBytes(outputPath, new byte[] { 0 });

        var result = await FileOperations.CompressFileAsync(
            input, outputPath: null, CompressionFormat.Gzip, level: 6, force: false, remove: false);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("output_exists", result.ExitReason);
        Assert.Null(result.Result);
        Assert.Contains("already exists", result.ErrorMessage);
    }

    [Fact]
    public async Task CompressFile_OutputExists_WithForce_Overwrites()
    {
        string input = CreateTestFile("data.txt");
        string outputPath = Path.Combine(_tempDir, "data.txt.gz");
        File.WriteAllBytes(outputPath, new byte[] { 0 });

        var result = await FileOperations.CompressFileAsync(
            input, outputPath: null, CompressionFormat.Gzip, level: 6, force: true, remove: false);

        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.Result);
        Assert.True(new FileInfo(outputPath).Length > 1, "Output should be overwritten with real compressed data");
    }

    [Fact]
    public async Task CompressFile_InputNotFound_ReturnsError()
    {
        string missing = Path.Combine(_tempDir, "nonexistent.txt");

        var result = await FileOperations.CompressFileAsync(
            missing, outputPath: null, CompressionFormat.Gzip, level: 6, force: false, remove: false);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("file_not_found", result.ExitReason);
        Assert.Null(result.Result);
    }

    [Theory]
    [InlineData(CompressionFormat.Gzip)]
    [InlineData(CompressionFormat.Brotli)]
    [InlineData(CompressionFormat.Zstd)]
    public async Task DecompressFile_RoundTrip_MatchesOriginal(CompressionFormat format)
    {
        string input = CreateTestFile("roundtrip.txt");
        byte[] originalData = File.ReadAllBytes(input);

        int level = CompressionFormatInfo.GetDefaultLevel(format);
        var compressResult = await FileOperations.CompressFileAsync(
            input, outputPath: null, format, level, force: false, remove: false);

        Assert.Equal(0, compressResult.ExitCode);

        string compressedPath = compressResult.Result!.OutputPath;
        // Original file still exists — need force to overwrite it during decompression
        var decompressResult = await FileOperations.DecompressFileAsync(
            compressedPath, outputPath: null, explicitFormat: null, force: true, remove: false);

        Assert.Equal(0, decompressResult.ExitCode);
        Assert.NotNull(decompressResult.Result);

        // The decompressed output path should match the original input path
        byte[] decompressedData = File.ReadAllBytes(decompressResult.Result!.OutputPath);
        Assert.Equal(originalData, decompressedData);
    }

    [Fact]
    public async Task DecompressFile_UnknownExtension_WithoutExplicitOutput_ReturnsError()
    {
        string input = CreateTestFile("data.bin");

        var result = await FileOperations.DecompressFileAsync(
            input, outputPath: null, explicitFormat: null, force: false, remove: false);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("unknown_extension", result.ExitReason);
        Assert.Null(result.Result);
        Assert.Contains("unknown extension", result.ErrorMessage);
    }

    [Fact]
    public async Task CompressFile_ExplicitOutputPath_Works()
    {
        string input = CreateTestFile("data.txt");
        string explicitOutput = Path.Combine(_tempDir, "custom_name.compressed");

        var result = await FileOperations.CompressFileAsync(
            input, outputPath: explicitOutput, CompressionFormat.Brotli, level: 6, force: false, remove: false);

        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.Result);
        Assert.Equal(explicitOutput, result.Result!.OutputPath);
        Assert.True(File.Exists(explicitOutput));
    }

    [Fact]
    public async Task DecompressFile_ExplicitOutputPath_Works()
    {
        string input = CreateTestFile("data.txt");

        var compressResult = await FileOperations.CompressFileAsync(
            input, outputPath: null, CompressionFormat.Gzip, level: 6, force: false, remove: false);

        Assert.Equal(0, compressResult.ExitCode);

        string explicitOutput = Path.Combine(_tempDir, "custom_decompressed.txt");
        var result = await FileOperations.DecompressFileAsync(
            compressResult.Result!.OutputPath, outputPath: explicitOutput, explicitFormat: null, force: false, remove: false);

        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.Result);
        Assert.Equal(explicitOutput, result.Result!.OutputPath);
        Assert.True(File.Exists(explicitOutput));

        byte[] original = File.ReadAllBytes(input);
        byte[] decompressed = File.ReadAllBytes(explicitOutput);
        Assert.Equal(original, decompressed);
    }

    [Fact]
    public async Task DecompressFile_OutputExists_WithoutForce_ReturnsError()
    {
        string input = CreateTestFile("data.txt");

        var compressResult = await FileOperations.CompressFileAsync(
            input, outputPath: null, CompressionFormat.Gzip, level: 6, force: false, remove: false);

        Assert.Equal(0, compressResult.ExitCode);

        // The decompressed output path would be "data.txt" which already exists
        var result = await FileOperations.DecompressFileAsync(
            compressResult.Result!.OutputPath, outputPath: null, explicitFormat: null, force: false, remove: false);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("output_exists", result.ExitReason);
    }

    [Fact]
    public async Task DecompressFile_InputNotFound_ReturnsError()
    {
        string missing = Path.Combine(_tempDir, "nonexistent.gz");

        var result = await FileOperations.DecompressFileAsync(
            missing, outputPath: null, explicitFormat: null, force: false, remove: false);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("file_not_found", result.ExitReason);
    }
}
