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
            JobIndex: 1, ChildExitCode: 42, Output: null,
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

    // -- Round 2: pin FaultMessage flowing through structured output. Round 1 added
    //    FaultMessage to JobResult; round 2 wired it through the formatters so JSON and
    //    NDJSON consumers can see fault diagnostics. --

    [Fact]
    public void FormatNdjsonLine_FaultedJob_IncludesFaultMessage()
    {
        var job = new JobResult(
            JobIndex: 7, ChildExitCode: -1, Output: null,
            Duration: TimeSpan.FromSeconds(0.05),
            SourceItems: new[] { "input" }, Skipped: false,
            FaultMessage: "failed to spawn 'foo': Win32Exception: No such file or directory");

        string line = Formatting.FormatNdjsonLine(job, 1, "child_failed", "wargs", Version);

        Assert.Contains("\"fault_message\":\"failed to spawn 'foo': Win32Exception: No such file or directory\"", line);
        Assert.Contains("\"job\":7", line);
    }

    [Fact]
    public void FormatNdjsonLine_NormalJob_OmitsFaultMessage()
    {
        var job = new JobResult(
            JobIndex: 1, ChildExitCode: 0, Output: null,
            Duration: TimeSpan.FromSeconds(0.1),
            SourceItems: new[] { "x" }, Skipped: false);

        string line = Formatting.FormatNdjsonLine(job, 0, "success", "wargs", Version);

        Assert.DoesNotContain("fault_message", line);
    }

    [Fact]
    public void FormatJson_AnyFaults_AppendsFaultsArray()
    {
        var jobs = new List<JobResult>
        {
            new(1, 0, "ok\n", TimeSpan.FromSeconds(0.1), new[] { "a" }, false),
            new(2, -1, null, TimeSpan.FromSeconds(0.05), new[] { "b" }, false,
                FaultMessage: "InvalidOperationException: empty FileName"),
            new(3, -1, null, TimeSpan.FromSeconds(0.06), new[] { "c" }, false,
                FaultMessage: "Win32Exception: command not found"),
        };
        var result = new WargsResult(3, 1, 2, 0, TimeSpan.FromSeconds(0.2), jobs);

        string json = Formatting.FormatJson(result, WargsExitCode.ChildFailed, "child_failed", "wargs", Version);

        Assert.Contains("\"faults\":[", json);
        Assert.Contains("\"job\":2", json);
        Assert.Contains("\"message\":\"InvalidOperationException: empty FileName\"", json);
        Assert.Contains("\"job\":3", json);
        Assert.Contains("\"message\":\"Win32Exception: command not found\"", json);
    }

    [Fact]
    public void FormatJson_NoFaults_OmitsFaultsArray()
    {
        var jobs = new List<JobResult>
        {
            new(1, 0, "ok\n", TimeSpan.FromSeconds(0.1), new[] { "a" }, false),
            new(2, 1, null, TimeSpan.FromSeconds(0.1), new[] { "b" }, false),
        };
        var result = new WargsResult(2, 1, 1, 0, TimeSpan.FromSeconds(0.2), jobs);

        string json = Formatting.FormatJson(result, WargsExitCode.ChildFailed, "child_failed", "wargs", Version);

        Assert.DoesNotContain("\"faults\"", json);
    }

    // -- Round-3 review: formatter contract pins. --

    [Fact]
    public void FormatNdjsonLine_FaultMessageWithJsonSpecials_IsEscapedAndStaysSingleLine()
    {
        // Real-world Win32Exception.Message and shell-error text can contain quotes,
        // newlines, tabs, and backslashes (especially Windows paths). Pin that JsonHelper
        // / Utf8JsonWriter escape correctly and the NDJSON line discipline (no embedded
        // raw newline) holds. Without this pin a future replacement of JsonHelper with a
        // hand-rolled writer could break NDJSON parseability in production.
        var job = new JobResult(
            JobIndex: 1, ChildExitCode: -1, Output: null,
            Duration: TimeSpan.FromSeconds(0.05),
            SourceItems: new[] { "x" }, Skipped: false,
            FaultMessage: "Win32Exception: \"quoted\"\nsecond line\twith\ttabs\\path");

        string line = Formatting.FormatNdjsonLine(job, 1, "child_failed", "wargs", Version);

        // NDJSON contract: must be a single line. Embedded \n in fault must be JSON-escaped.
        Assert.DoesNotContain('\n', line.TrimEnd('\r', '\n'));
        Assert.Contains("fault_message", line);

        using var doc = System.Text.Json.JsonDocument.Parse(line);
        string parsed = doc.RootElement.GetProperty("fault_message").GetString()!;
        Assert.Equal("Win32Exception: \"quoted\"\nsecond line\twith\ttabs\\path", parsed);
    }

    [Fact]
    public void FormatJsonError_NoInput_HasExpectedShape()
    {
        // Round-2 reused FormatJsonError to emit the no_input envelope under --ndjson and
        // the input_read_failed envelope under --json/--ndjson. Pin the exact shape so a
        // future change to FormatJsonError doesn't accidentally break those callers'
        // contract.
        string json = Formatting.FormatJsonError(0, "no_input", "wargs", "0.1.0");

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("wargs", root.GetProperty("tool").GetString());
        Assert.Equal("0.1.0", root.GetProperty("version").GetString());
        Assert.Equal(0, root.GetProperty("exit_code").GetInt32());
        Assert.Equal("no_input", root.GetProperty("exit_reason").GetString());

        int fieldCount = 0;
        foreach (var _ in root.EnumerateObject()) { fieldCount++; }
        Assert.Equal(4, fieldCount);
    }

    // -- Round-4 review: pin the round-4-added exit_reason values. The same
    //    FormatJsonError shape is reused across all of them; these tests catch a
    //    future change that adds an extra field or renames an existing one. --

    [Theory]
    [InlineData(130, "cancelled")]
    [InlineData(126, "unexpected_error")]
    [InlineData(0, "dry_run")]
    public void FormatJsonError_Round4ExitReasons_ParseAndContainExpectedFields(int exitCode, string exitReason)
    {
        string json = Formatting.FormatJsonError(exitCode, exitReason, "wargs", "0.1.0");

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(exitCode, root.GetProperty("exit_code").GetInt32());
        Assert.Equal(exitReason, root.GetProperty("exit_reason").GetString());
        Assert.Equal("wargs", root.GetProperty("tool").GetString());

        int fieldCount = 0;
        foreach (var _ in root.EnumerateObject()) { fieldCount++; }
        Assert.Equal(4, fieldCount);
    }

    [Fact]
    public void FormatJson_DryRunEnvelope_ReportsTotalJobsCount()
    {
        // Round-4 --dry-run path emits FormatJson with TotalJobs reflecting the would-be
        // invocation count (so callers can tell whether anything would have run) and
        // exit_reason="dry_run". Pin the shape.
        var result = new WargsResult(
            TotalJobs: 7, Succeeded: 0, Failed: 0, Skipped: 0,
            WallTime: TimeSpan.Zero,
            Jobs: new List<JobResult>());

        string json = Formatting.FormatJson(result, 0, "dry_run", "wargs", Version);

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(0, root.GetProperty("exit_code").GetInt32());
        Assert.Equal("dry_run", root.GetProperty("exit_reason").GetString());
        Assert.Equal(7, root.GetProperty("total_jobs").GetInt32());
        Assert.Equal(0, root.GetProperty("succeeded").GetInt32());
        Assert.Equal(0, root.GetProperty("failed").GetInt32());
    }
}
