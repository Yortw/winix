using Winix.Wargs;
using Xunit;

namespace Winix.Wargs.Tests;

public class FormattingTests
{
    private static readonly string Version = "0.1.0";

    [Fact]
    public void FormatJson_Success_IncludesStandardFields()
    {
        var result = new WargsResult(
            TotalJobs: 3, Succeeded: 3, Failed: 0, Skipped: 0,
            WallTime: TimeSpan.FromSeconds(1.5),
            Jobs: new List<JobResult>());

        string json = Formatting.FormatJson(result, 0, "success", "wargs", Version);

        Assert.Contains("\"tool\":\"wargs\"", json);
        Assert.Contains("\"version\":\"0.1.0\"", json);
        Assert.Contains("\"exit_code\":0", json);
        Assert.Contains("\"exit_reason\":\"success\"", json);
        Assert.Contains("\"total_jobs\":3", json);
        Assert.Contains("\"succeeded\":3", json);
        Assert.Contains("\"failed\":0", json);
        Assert.Contains("\"skipped\":0", json);
        Assert.Contains("\"wall_seconds\":", json);
    }

    [Fact]
    public void FormatJson_Failure_ReflectsExitCode()
    {
        var result = new WargsResult(
            TotalJobs: 2, Succeeded: 1, Failed: 1, Skipped: 0,
            WallTime: TimeSpan.FromSeconds(2.0),
            Jobs: new List<JobResult>());

        string json = Formatting.FormatJson(result, WargsExitCode.ChildFailed, "child_failed", "wargs", Version);

        Assert.Contains("\"exit_code\":123", json);
        Assert.Contains("\"exit_reason\":\"child_failed\"", json);
        Assert.Contains("\"failed\":1", json);
    }

    [Fact]
    public void FormatNdjsonLine_SingleItem_InputIsString()
    {
        var job = new JobResult(
            JobIndex: 1, ChildExitCode: 0, Output: "hello\n",
            Duration: TimeSpan.FromSeconds(0.34),
            SourceItems: new[] { "file1.cs" }, Skipped: false);

        string line = Formatting.FormatNdjsonLine(job, 0, "success", "wargs", Version);

        Assert.Contains("\"job\":1", line);
        Assert.Contains("\"child_exit_code\":0", line);
        Assert.Contains("\"input\":\"file1.cs\"", line);
        Assert.Contains("\"wall_seconds\":", line);
        // NDJSON line should not contain internal newlines
        Assert.DoesNotContain("\n", line.TrimEnd('\r', '\n'));
    }

    [Fact]
    public void FormatNdjsonLine_MultipleItems_InputIsArray()
    {
        var job = new JobResult(
            JobIndex: 2, ChildExitCode: 0, Output: null,
            Duration: TimeSpan.FromSeconds(0.5),
            SourceItems: new[] { "a", "b", "c" }, Skipped: false);

        string line = Formatting.FormatNdjsonLine(job, 0, "success", "wargs", Version);

        Assert.Contains("\"input\":[\"a\",\"b\",\"c\"]", line);
    }

    [Fact]
    public void FormatNdjsonLine_FailedJob_ExitCodeReflectsFailure()
    {
        var job = new JobResult(
            JobIndex: 0, ChildExitCode: 42, Output: null,
            Duration: TimeSpan.FromSeconds(0.1),
            SourceItems: new[] { "bad.cs" }, Skipped: false);

        int exitCode = job.ChildExitCode == 0 ? 0 : 1;
        string exitReason = job.ChildExitCode == 0 ? "success" : "child_failed";
        string line = Formatting.FormatNdjsonLine(job, exitCode, exitReason, "wargs", Version);

        Assert.Contains("\"exit_code\":1", line);
        Assert.Contains("\"exit_reason\":\"child_failed\"", line);
        Assert.Contains("\"child_exit_code\":42", line);
    }

    [Fact]
    public void FormatJsonError_ReturnsValidJson()
    {
        string json = Formatting.FormatJsonError(125, "usage_error", "wargs", Version);

        Assert.Contains("\"tool\":\"wargs\"", json);
        Assert.Contains("\"exit_code\":125", json);
        Assert.Contains("\"exit_reason\":\"usage_error\"", json);
    }

    [Fact]
    public void FormatHumanSummary_NoFailures_ReturnsNull()
    {
        var result = new WargsResult(3, 3, 0, 0, TimeSpan.FromSeconds(1), new List<JobResult>());
        Assert.Null(Formatting.FormatHumanSummary(result));
    }

    [Fact]
    public void FormatHumanSummary_WithFailures_ReturnsMessage()
    {
        var result = new WargsResult(10, 7, 3, 0, TimeSpan.FromSeconds(5), new List<JobResult>());
        string? summary = Formatting.FormatHumanSummary(result);

        Assert.NotNull(summary);
        Assert.Contains("3", summary);
        Assert.Contains("10", summary);
    }
}
