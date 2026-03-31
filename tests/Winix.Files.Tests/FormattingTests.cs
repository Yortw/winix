#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Winix.FileWalk;
using Winix.Files;
using Xunit;

namespace Winix.Files.Tests;

public class FormattingTests
{
    private const string ToolName = "files";
    private const string Version = "0.1.0";

    private static readonly FileEntry SampleFile = new(
        Path: "src/Program.cs", Name: "Program.cs", Type: FileEntryType.File,
        SizeBytes: 2340, Modified: new DateTimeOffset(2026, 3, 31, 14, 22, 0, TimeSpan.FromHours(13)),
        Depth: 1, IsText: null);

    private static readonly FileEntry SampleDir = new(
        Path: "src", Name: "src", Type: FileEntryType.Directory,
        SizeBytes: -1, Modified: new DateTimeOffset(2026, 3, 31, 10, 0, 0, TimeSpan.FromHours(13)),
        Depth: 0, IsText: null);

    private static readonly FileEntry SampleSymlink = new(
        Path: "bin/latest", Name: "latest", Type: FileEntryType.Symlink,
        SizeBytes: 0, Modified: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Depth: 1, IsText: null);

    // --- FormatPath ---

    [Fact]
    public void FormatPath_ReturnsEntryPath()
    {
        Assert.Equal("src/Program.cs", Formatting.FormatPath(SampleFile));
    }

    [Fact]
    public void FormatPath_DirectoryReturnsDirectoryPath()
    {
        Assert.Equal("src", Formatting.FormatPath(SampleDir));
    }

    // --- FormatTypeString ---

    [Fact]
    public void FormatTypeString_File_ReturnsFile()
    {
        Assert.Equal("file", Formatting.FormatTypeString(FileEntryType.File));
    }

    [Fact]
    public void FormatTypeString_Directory_ReturnsDir()
    {
        Assert.Equal("dir", Formatting.FormatTypeString(FileEntryType.Directory));
    }

    [Fact]
    public void FormatTypeString_Symlink_ReturnsLink()
    {
        Assert.Equal("link", Formatting.FormatTypeString(FileEntryType.Symlink));
    }

    // --- FormatLong ---

    [Fact]
    public void FormatLong_File_IncludesSizeAndDate()
    {
        string line = Formatting.FormatLong(SampleFile);

        // Path is first field
        Assert.StartsWith("src/Program.cs\t", line);
        // Size formatted with commas
        Assert.Contains("2,340", line);
        // Type string
        Assert.Contains("file", line);
    }

    [Fact]
    public void FormatLong_Directory_UsesDashForSize()
    {
        string line = Formatting.FormatLong(SampleDir);

        string[] parts = line.Split('\t');
        // size field (index 1) should be dash for directories
        Assert.Equal("-", parts[1]);
    }

    [Fact]
    public void FormatLong_UsesTabDelimiters()
    {
        string line = Formatting.FormatLong(SampleFile);

        // path TAB size TAB date TAB type — exactly 3 tabs
        Assert.Equal(3, line.Split('\t').Length - 1);
    }

    [Fact]
    public void FormatLong_DateUsesLocalTime()
    {
        // The sample file has Modified with +13:00 offset.
        // LocalDateTime gives the wall-clock time at that offset: 2026-03-31 14:22.
        string line = Formatting.FormatLong(SampleFile);
        string localDate = SampleFile.Modified.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        Assert.Contains(localDate, line);
    }

    [Fact]
    public void FormatLong_TypeStringIsLastField()
    {
        string line = Formatting.FormatLong(SampleFile);
        string[] parts = line.Split('\t');

        Assert.Equal("file", parts[^1]);
    }

    // --- FormatNdjsonLine ---

    [Fact]
    public void FormatNdjsonLine_ContainsStandardEnvelopeFields()
    {
        string line = Formatting.FormatNdjsonLine(SampleFile, ToolName, Version);

        Assert.Contains("\"tool\":\"files\"", line);
        Assert.Contains("\"version\":\"0.1.0\"", line);
        Assert.Contains("\"exit_code\":0", line);
        Assert.Contains("\"exit_reason\":\"success\"", line);
    }

    [Fact]
    public void FormatNdjsonLine_ContainsAllFileEntryFields()
    {
        string line = Formatting.FormatNdjsonLine(SampleFile, ToolName, Version);

        Assert.Contains("\"path\":\"src/Program.cs\"", line);
        Assert.Contains("\"name\":\"Program.cs\"", line);
        Assert.Contains("\"type\":\"file\"", line);
        Assert.Contains("\"size_bytes\":2340", line);
        // ISO 8601 modified date
        Assert.Contains("\"modified\":", line);
        Assert.Contains("\"depth\":1", line);
    }

    [Fact]
    public void FormatNdjsonLine_IsTextOmittedWhenNull()
    {
        // SampleFile has IsText: null — field must not appear
        string line = Formatting.FormatNdjsonLine(SampleFile, ToolName, Version);

        Assert.DoesNotContain("is_text", line);
    }

    [Fact]
    public void FormatNdjsonLine_IsTextPresentWhenSet()
    {
        var entry = SampleFile with { IsText = true };
        string line = Formatting.FormatNdjsonLine(entry, ToolName, Version);

        Assert.Contains("\"is_text\":true", line);
    }

    [Fact]
    public void FormatNdjsonLine_IsValidJson()
    {
        string line = Formatting.FormatNdjsonLine(SampleFile, ToolName, Version);

        // Will throw if invalid — that's the assertion
        JsonDocument.Parse(line);
    }

    [Fact]
    public void FormatNdjsonLine_ContainsNoInternalNewlines()
    {
        string line = Formatting.FormatNdjsonLine(SampleFile, ToolName, Version);

        Assert.DoesNotContain('\n', line);
    }

    [Fact]
    public void FormatNdjsonLine_PathWithSpecialChars_EscapesCorrectly()
    {
        var entry = SampleFile with { Path = "src\\some\"file.cs" };
        string line = Formatting.FormatNdjsonLine(entry, ToolName, Version);

        // Must be valid JSON even with backslash and quote in the path
        JsonDocument.Parse(line);
    }

    // --- FormatJsonSummary ---

    [Fact]
    public void FormatJsonSummary_ContainsStandardFields()
    {
        var roots = new List<string> { "src", "tests" };
        string json = Formatting.FormatJsonSummary(42, roots, 0, "success", ToolName, Version);

        Assert.Contains("\"tool\":\"files\"", json);
        Assert.Contains("\"version\":\"0.1.0\"", json);
        Assert.Contains("\"exit_code\":0", json);
        Assert.Contains("\"exit_reason\":\"success\"", json);
        Assert.Contains("\"count\":42", json);
        Assert.Contains("\"searched_roots\":", json);
    }

    [Fact]
    public void FormatJsonSummary_IsValidJson()
    {
        var roots = new List<string> { "." };
        string json = Formatting.FormatJsonSummary(10, roots, 0, "success", ToolName, Version);

        JsonDocument.Parse(json);
    }

    // --- FormatJsonError ---

    [Fact]
    public void FormatJsonError_ContainsExitCodeAndReason()
    {
        string json = Formatting.FormatJsonError(125, "usage_error", ToolName, Version);

        Assert.Contains("\"tool\":\"files\"", json);
        Assert.Contains("\"exit_code\":125", json);
        Assert.Contains("\"exit_reason\":\"usage_error\"", json);
    }

    [Fact]
    public void FormatJsonError_IsValidJson()
    {
        string json = Formatting.FormatJsonError(126, "not_found", ToolName, Version);

        JsonDocument.Parse(json);
    }
}
