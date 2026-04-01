using Xunit;
using Winix.Squeeze;

namespace Winix.Squeeze.Tests;

public class FormatHumanStatsTests
{
    [Fact]
    public void SingleFile_ShowsExpectedFormat()
    {
        var result = new SqueezeResult(
            InputPath: "/tmp/data.txt",
            OutputPath: "/tmp/data.txt.gz",
            InputBytes: 1048576,
            OutputBytes: 524288,
            Format: CompressionFormat.Gzip,
            Elapsed: TimeSpan.FromSeconds(0.12));

        string output = Formatting.FormatHuman(result, useColor: false);

        Assert.Contains("data.txt", output);
        Assert.Contains("1.0 MB", output);
        Assert.Contains("512.0 KB", output);
        Assert.Contains("50.0%", output);
        Assert.Contains("gz", output);
        Assert.Contains("0.120s", output);
        Assert.Contains("\u2192", output);
    }

    [Fact]
    public void HighRatio_ShowsGreenColorWhenEnabled()
    {
        var result = new SqueezeResult(
            InputPath: "/tmp/data.txt",
            OutputPath: "/tmp/data.txt.gz",
            InputBytes: 1000,
            OutputBytes: 400,
            Format: CompressionFormat.Gzip,
            Elapsed: TimeSpan.FromSeconds(0.01));

        string output = Formatting.FormatHuman(result, useColor: true);

        // 60% ratio (> 50%) should include green ANSI escape
        Assert.Contains("\x1b[32m", output);
    }

    [Fact]
    public void LowRatio_NoGreenColor()
    {
        var result = new SqueezeResult(
            InputPath: "/tmp/data.txt",
            OutputPath: "/tmp/data.txt.gz",
            InputBytes: 1000,
            OutputBytes: 800,
            Format: CompressionFormat.Gzip,
            Elapsed: TimeSpan.FromSeconds(0.01));

        string output = Formatting.FormatHuman(result, useColor: true);

        // 20% ratio (< 50%) should not include green for the ratio
        // The ratio text itself should not be preceded by green
        string ratioSection = "20.0%";
        int ratioIndex = output.IndexOf(ratioSection);
        Assert.True(ratioIndex > 0);
        // Check that green escape does not appear immediately before the ratio
        string beforeRatio = output.Substring(0, ratioIndex);
        Assert.DoesNotContain("\x1b[32m", beforeRatio.Substring(beforeRatio.LastIndexOf('(') + 1));
    }

    [Fact]
    public void ZeroInputBytes_ShowsZeroRatio()
    {
        var result = new SqueezeResult(
            InputPath: "/tmp/empty.txt",
            OutputPath: "/tmp/empty.txt.gz",
            InputBytes: 0,
            OutputBytes: 20,
            Format: CompressionFormat.Gzip,
            Elapsed: TimeSpan.FromSeconds(0.001));

        string output = Formatting.FormatHuman(result, useColor: false);

        Assert.Contains("0.0%", output);
    }
}

public class FormatJsonTests
{
    [Fact]
    public void StandardFields_Present()
    {
        var results = new[]
        {
            new SqueezeResult(
                InputPath: "/tmp/data.txt",
                OutputPath: "/tmp/data.txt.gz",
                InputBytes: 1000,
                OutputBytes: 500,
                Format: CompressionFormat.Gzip,
                Elapsed: TimeSpan.FromSeconds(0.123))
        };

        string json = Formatting.FormatJson(results, exitCode: 0, exitReason: "success", toolName: "squeeze", version: "0.1.0");

        Assert.Contains("\"tool\":\"squeeze\"", json);
        Assert.Contains("\"version\":\"0.1.0\"", json);
        Assert.Contains("\"exit_code\":0", json);
        Assert.Contains("\"exit_reason\":\"success\"", json);
        Assert.Contains("\"files\":[", json);
    }

    [Fact]
    public void FileFields_Present()
    {
        var results = new[]
        {
            new SqueezeResult(
                InputPath: "/tmp/data.txt",
                OutputPath: "/tmp/data.txt.gz",
                InputBytes: 1000,
                OutputBytes: 500,
                Format: CompressionFormat.Gzip,
                Elapsed: TimeSpan.FromSeconds(0.123))
        };

        string json = Formatting.FormatJson(results, exitCode: 0, exitReason: "success", toolName: "squeeze", version: "0.1.0");

        Assert.Contains("\"input\":\"/tmp/data.txt\"", json);
        Assert.Contains("\"output\":\"/tmp/data.txt.gz\"", json);
        Assert.Contains("\"input_bytes\":1000", json);
        Assert.Contains("\"output_bytes\":500", json);
        Assert.Contains("\"ratio\":0.500", json);
        Assert.Contains("\"format\":\"gz\"", json);
        Assert.Contains("\"seconds\":0.123", json);
    }

    [Fact]
    public void MultipleFiles_AllPresent()
    {
        var results = new[]
        {
            new SqueezeResult(
                InputPath: "a.txt",
                OutputPath: "a.txt.gz",
                InputBytes: 100,
                OutputBytes: 50,
                Format: CompressionFormat.Gzip,
                Elapsed: TimeSpan.FromSeconds(0.01)),
            new SqueezeResult(
                InputPath: "b.txt",
                OutputPath: "b.txt.br",
                InputBytes: 200,
                OutputBytes: 80,
                Format: CompressionFormat.Brotli,
                Elapsed: TimeSpan.FromSeconds(0.02))
        };

        string json = Formatting.FormatJson(results, exitCode: 0, exitReason: "success", toolName: "squeeze", version: "0.1.0");

        Assert.Contains("\"input\":\"a.txt\"", json);
        Assert.Contains("\"input\":\"b.txt\"", json);
        Assert.Contains("\"format\":\"gz\"", json);
        Assert.Contains("\"format\":\"br\"", json);
    }

    [Fact]
    public void PipeMode_EscapesAngleBrackets()
    {
        var results = new[]
        {
            new SqueezeResult(
                InputPath: "<stdin>",
                OutputPath: "<stdout>",
                InputBytes: 1000,
                OutputBytes: 500,
                Format: CompressionFormat.Gzip,
                Elapsed: TimeSpan.FromSeconds(0.1))
        };

        string json = Formatting.FormatJson(results, exitCode: 0, exitReason: "success", toolName: "squeeze", version: "0.1.0");

        Assert.Contains("\"input\":\"<stdin>\"", json);
        Assert.Contains("\"output\":\"<stdout>\"", json);
    }

    [Fact]
    public void ZeroInputBytes_RatioIsZero()
    {
        var results = new[]
        {
            new SqueezeResult(
                InputPath: "empty.txt",
                OutputPath: "empty.txt.gz",
                InputBytes: 0,
                OutputBytes: 20,
                Format: CompressionFormat.Gzip,
                Elapsed: TimeSpan.FromSeconds(0.001))
        };

        string json = Formatting.FormatJson(results, exitCode: 0, exitReason: "success", toolName: "squeeze", version: "0.1.0");

        Assert.Contains("\"ratio\":0.000", json);
    }

    [Fact]
    public void BackslashPaths_AreEscaped()
    {
        var results = new[]
        {
            new SqueezeResult(
                InputPath: "C:\\Users\\data.txt",
                OutputPath: "C:\\Users\\data.txt.gz",
                InputBytes: 1000,
                OutputBytes: 500,
                Format: CompressionFormat.Gzip,
                Elapsed: TimeSpan.FromSeconds(0.1))
        };

        string json = Formatting.FormatJson(results, exitCode: 0, exitReason: "success", toolName: "squeeze", version: "0.1.0");

        Assert.Contains("C:\\\\Users\\\\data.txt", json);
    }
}

public class FormatJsonWithErrorsTests
{
    [Fact]
    public void PartialFailure_IncludesErrorsInSameEnvelope()
    {
        var results = new[]
        {
            new SqueezeResult("a.txt", "a.txt.gz", 1000, 500,
                CompressionFormat.Gzip, TimeSpan.FromMilliseconds(50))
        };
        var errors = new[] { "squeeze: b.txt: No such file" };

        string json = Formatting.FormatJson(results, exitCode: 1, exitReason: "partial_failure",
            toolName: "squeeze", version: "0.1.0", errors: errors);

        // Single document with both files and errors arrays
        Assert.Contains("\"files\":", json);
        Assert.Contains("\"errors\":", json);
        Assert.Contains("\"exit_reason\":\"partial_failure\"", json);
        Assert.Contains("squeeze: b.txt: No such file", json);
        Assert.Contains("\"input\":\"a.txt\"", json);
    }

    [Fact]
    public void NoErrors_OmitsErrorsArray()
    {
        var results = new[]
        {
            new SqueezeResult("a.txt", "a.txt.gz", 1000, 500,
                CompressionFormat.Gzip, TimeSpan.FromMilliseconds(50))
        };

        string json = Formatting.FormatJson(results, exitCode: 0, exitReason: "success",
            toolName: "squeeze", version: "0.1.0", errors: null);

        Assert.Contains("\"files\":", json);
        Assert.DoesNotContain("\"errors\":", json);
    }
}

public class FormatJsonErrorTests
{
    [Fact]
    public void CorruptInput_ShowsErrorFields()
    {
        string json = Formatting.FormatJsonError(exitCode: 1, exitReason: "corrupt_input", toolName: "squeeze", version: "0.1.0");

        Assert.Contains("\"tool\":\"squeeze\"", json);
        Assert.Contains("\"version\":\"0.1.0\"", json);
        Assert.Contains("\"exit_code\":1", json);
        Assert.Contains("\"exit_reason\":\"corrupt_input\"", json);
        Assert.DoesNotContain("\"files\"", json);
    }

    [Fact]
    public void UsageError_ShowsErrorFields()
    {
        string json = Formatting.FormatJsonError(exitCode: 125, exitReason: "usage_error", toolName: "squeeze", version: "0.1.0");

        Assert.Contains("\"exit_code\":125", json);
        Assert.Contains("\"exit_reason\":\"usage_error\"", json);
        Assert.DoesNotContain("\"files\"", json);
    }
}
