#nullable enable

using System.Collections.Generic;
using System.Text.Json;
using Winix.WhoHolds;
using Xunit;

namespace Winix.WhoHolds.Tests;

public sealed class FormattingTests
{
    // -----------------------------------------------------------------------
    // FormatTable
    // -----------------------------------------------------------------------

    [Fact]
    public void FormatTable_SingleResult_ContainsPidAndName()
    {
        var results = new List<LockInfo>
        {
            new LockInfo(1234, "devenv.exe", @"D:\test.dll")
        };

        string output = Formatting.FormatTable(results, useColor: false);

        Assert.Contains("1234", output);
        Assert.Contains("devenv.exe", output);
        Assert.Contains(@"D:\test.dll", output);
    }

    [Fact]
    public void FormatTable_MultipleResults_ContainsAllPids()
    {
        var results = new List<LockInfo>
        {
            new LockInfo(1234, "devenv.exe", @"D:\test.dll"),
            new LockInfo(5678, "msbuild.exe", @"D:\test.dll")
        };

        string output = Formatting.FormatTable(results, useColor: false);

        Assert.Contains("1234", output);
        Assert.Contains("devenv.exe", output);
        Assert.Contains("5678", output);
        Assert.Contains("msbuild.exe", output);
    }

    [Fact]
    public void FormatTable_WithColor_ContainsAnsiEscapes()
    {
        var results = new List<LockInfo>
        {
            new LockInfo(1234, "devenv.exe", @"D:\test.dll")
        };

        string output = Formatting.FormatTable(results, useColor: true);

        Assert.Contains("\x1b[", output);
    }

    [Fact]
    public void FormatTable_NoColor_NoAnsiEscapes()
    {
        var results = new List<LockInfo>
        {
            new LockInfo(1234, "devenv.exe", @"D:\test.dll")
        };

        string output = Formatting.FormatTable(results, useColor: false);

        Assert.DoesNotContain("\x1b[", output);
    }

    [Fact]
    public void FormatTable_PortResult_ShowsProtocol()
    {
        var results = new List<LockInfo>
        {
            new LockInfo(1234, "system", "TCP :8080")
        };

        string output = Formatting.FormatTable(results, useColor: false);

        Assert.Contains("TCP :8080", output);
    }

    [Fact]
    public void FormatTable_WithFullPath_ShowsPath()
    {
        var results = new List<LockInfo>
        {
            new LockInfo(1234, "devenv.exe", @"D:\test.dll", @"C:\Program Files\VS\devenv.exe")
        };

        string output = Formatting.FormatTable(results, useColor: false, showFullPath: true);

        // The full path should appear in the output.
        Assert.Contains(@"C:\Program Files\VS\devenv.exe", output);
        // The header "Process" should still be present.
        Assert.Contains("Process", output);
    }

    [Fact]
    public void FormatTable_WithFullPath_FallsBackToNameWhenPathEmpty()
    {
        var results = new List<LockInfo>
        {
            new LockInfo(1234, "devenv.exe", @"D:\test.dll", "")
        };

        string output = Formatting.FormatTable(results, useColor: false, showFullPath: true);

        Assert.Contains("devenv.exe", output);
    }

    [Fact]
    public void FormatTable_WithState_ShowsStateColumn()
    {
        var results = new List<LockInfo>
        {
            new LockInfo(1234, "system", "TCP :80", "", "LISTEN")
        };

        string output = Formatting.FormatTable(results, useColor: false);

        Assert.Contains("State", output);
        Assert.Contains("LISTEN", output);
    }

    [Fact]
    public void FormatTable_NoState_OmitsStateColumn()
    {
        var results = new List<LockInfo>
        {
            new LockInfo(1234, "devenv.exe", @"D:\test.dll")
        };

        string output = Formatting.FormatTable(results, useColor: false);

        Assert.DoesNotContain("State", output);
    }

    // -----------------------------------------------------------------------
    // FormatPidOnly
    // -----------------------------------------------------------------------

    [Fact]
    public void FormatPidOnly_SingleResult_OnePidPerLine()
    {
        var results = new List<LockInfo>
        {
            new LockInfo(1234, "devenv.exe", @"D:\test.dll")
        };

        string output = Formatting.FormatPidOnly(results);

        Assert.Equal("1234", output.Trim());
    }

    [Fact]
    public void FormatPidOnly_MultipleResults_OnePidPerLine()
    {
        var results = new List<LockInfo>
        {
            new LockInfo(1234, "devenv.exe", @"D:\test.dll"),
            new LockInfo(5678, "msbuild.exe", @"D:\test.dll")
        };

        string output = Formatting.FormatPidOnly(results);
        string[] lines = output.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.Contains("1234", lines[0].Trim());
        Assert.Contains("5678", lines[1].Trim());
    }

    // -----------------------------------------------------------------------
    // FormatElevationWarning
    // -----------------------------------------------------------------------

    [Fact]
    public void FormatElevationWarning_WithColor_ContainsYellowEscape()
    {
        string output = Formatting.FormatElevationWarning(useColor: true);

        Assert.Contains("\x1b[33m", output);
        Assert.Contains("Not elevated", output);
    }

    [Fact]
    public void FormatElevationWarning_NoColor_PlainText()
    {
        string output = Formatting.FormatElevationWarning(useColor: false);

        Assert.DoesNotContain("\x1b[", output);
        Assert.Contains("Not elevated", output);
    }

    // -----------------------------------------------------------------------
    // FormatNoResults
    // -----------------------------------------------------------------------

    [Fact]
    public void FormatNoResults_ContainsResource()
    {
        string output = Formatting.FormatNoResults(@"D:\test.dll");

        Assert.Contains(@"D:\test.dll", output);
        Assert.Contains("No processes found", output);
    }

    [Fact]
    public void FormatNoResults_Port_ContainsPort()
    {
        string output = Formatting.FormatNoResults(":8080");

        Assert.Contains(":8080", output);
    }

    // -----------------------------------------------------------------------
    // FormatJson
    // -----------------------------------------------------------------------

    [Fact]
    public void FormatJson_ReturnsValidJson()
    {
        var results = new List<LockInfo>
        {
            new LockInfo(1234, "devenv.exe", @"D:\test.dll")
        };

        string output = Formatting.FormatJson(results, exitCode: 0, exitReason: "success", toolName: "whoholds", version: "1.0.0");

        // Must be valid JSON
        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        Assert.Equal("whoholds", root.GetProperty("tool").GetString());
        Assert.Equal("1.0.0", root.GetProperty("version").GetString());
        Assert.True(root.TryGetProperty("exit_code", out _));
        Assert.True(root.TryGetProperty("exit_reason", out _));
        Assert.True(root.TryGetProperty("processes", out var processes));

        var first = processes.EnumerateArray().GetEnumerator();
        Assert.True(first.MoveNext());
        Assert.True(first.Current.TryGetProperty("pid", out _));
        Assert.True(first.Current.TryGetProperty("name", out _));
        Assert.True(first.Current.TryGetProperty("path", out _));
        Assert.True(first.Current.TryGetProperty("state", out _));
        Assert.True(first.Current.TryGetProperty("resource", out _));
    }

    // -----------------------------------------------------------------------
    // FormatJsonError
    // -----------------------------------------------------------------------

    [Fact]
    public void FormatJsonError_ReturnsValidJson()
    {
        string output = Formatting.FormatJsonError(exitCode: 125, exitReason: "bad_argument", toolName: "whoholds", version: "1.0.0");

        // Must be valid JSON
        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        Assert.Equal("whoholds", root.GetProperty("tool").GetString());
        Assert.True(root.TryGetProperty("exit_code", out _));
        Assert.True(root.TryGetProperty("exit_reason", out _));
    }
}
