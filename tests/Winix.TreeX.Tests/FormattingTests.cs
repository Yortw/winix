#nullable enable

using System;
using System.Text.Json;
using Winix.FileWalk;
using Winix.TreeX;
using Xunit;

namespace Winix.TreeX.Tests;

public class FormattingTests
{
    private const string ToolName = "treex";
    private const string Version = "0.1.0";

    private static readonly DateTimeOffset FixedDate = new(2026, 3, 31, 14, 22, 0, TimeSpan.FromHours(13));

    private static TreeNode MakeFile(string name, long size)
    {
        return new TreeNode
        {
            Name = name,
            FullPath = $"/tmp/{name}",
            Type = FileEntryType.File,
            SizeBytes = size,
            Modified = FixedDate
        };
    }

    private static TreeNode MakeDir(string name, long sizeBytes = -1)
    {
        return new TreeNode
        {
            Name = name,
            FullPath = $"/tmp/{name}",
            Type = FileEntryType.Directory,
            SizeBytes = sizeBytes,
            Modified = FixedDate
        };
    }

    private static TreeNode MakeSymlink(string name)
    {
        return new TreeNode
        {
            Name = name,
            FullPath = $"/tmp/{name}",
            Type = FileEntryType.Symlink,
            SizeBytes = 100,
            Modified = FixedDate
        };
    }

    // --- FormatNdjsonLine ---

    [Fact]
    public void FormatNdjsonLine_ContainsStandardEnvelopeFields()
    {
        TreeNode node = MakeFile("app.cs", 2340);
        string line = Formatting.FormatNdjsonLine(node, 1, "/tmp", ToolName, Version);

        Assert.Contains("\"tool\":\"treex\"", line);
        Assert.Contains("\"version\":\"0.1.0\"", line);
        Assert.Contains("\"exit_code\":0", line);
        Assert.Contains("\"exit_reason\":\"success\"", line);
    }

    [Fact]
    public void FormatNdjsonLine_ContainsAllNodeFields()
    {
        TreeNode node = MakeFile("app.cs", 2340);
        string line = Formatting.FormatNdjsonLine(node, 3, "/tmp", ToolName, Version);

        Assert.Contains("\"path\":\"app.cs\"", line);
        Assert.Contains("\"name\":\"app.cs\"", line);
        Assert.Contains("\"type\":\"file\"", line);
        Assert.Contains("\"size_bytes\":2340", line);
        Assert.Contains("\"modified\":", line);
        Assert.Contains("\"depth\":3", line);
    }

    [Fact]
    public void FormatNdjsonLine_DirectoryType_EmitsDir()
    {
        TreeNode node = MakeDir("src");
        string line = Formatting.FormatNdjsonLine(node, 0, "/tmp", ToolName, Version);

        Assert.Contains("\"type\":\"dir\"", line);
    }

    [Fact]
    public void FormatNdjsonLine_SymlinkType_EmitsLink()
    {
        TreeNode node = MakeSymlink("latest");
        string line = Formatting.FormatNdjsonLine(node, 1, "/tmp", ToolName, Version);

        Assert.Contains("\"type\":\"link\"", line);
    }

    [Fact]
    public void FormatNdjsonLine_IsValidJson()
    {
        TreeNode node = MakeFile("app.cs", 2340);
        string line = Formatting.FormatNdjsonLine(node, 1, "/tmp", ToolName, Version);

        // Will throw if invalid
        JsonDocument.Parse(line);
    }

    [Fact]
    public void FormatNdjsonLine_ContainsNoNewlines()
    {
        TreeNode node = MakeFile("app.cs", 2340);
        string line = Formatting.FormatNdjsonLine(node, 1, "/tmp", ToolName, Version);

        Assert.DoesNotContain('\n', line);
    }

    [Fact]
    public void FormatNdjsonLine_SpecialCharsInPath_EscapesCorrectly()
    {
        TreeNode node = new TreeNode
        {
            Name = "some\"file.cs",
            FullPath = "/tmp/some\"file.cs",
            Type = FileEntryType.File,
            SizeBytes = 100,
            Modified = FixedDate
        };

        string line = Formatting.FormatNdjsonLine(node, 0, "/tmp", ToolName, Version);

        // Must be valid JSON even with quote in the path
        JsonDocument.Parse(line);
    }

    // --- FormatJsonSummary ---

    [Fact]
    public void FormatJsonSummary_ContainsStandardFields()
    {
        var stats = new TreeStats(3, 10, 49356);
        string json = Formatting.FormatJsonSummary(stats, 0, "success", ToolName, Version);

        Assert.Contains("\"tool\":\"treex\"", json);
        Assert.Contains("\"version\":\"0.1.0\"", json);
        Assert.Contains("\"exit_code\":0", json);
        Assert.Contains("\"exit_reason\":\"success\"", json);
    }

    [Fact]
    public void FormatJsonSummary_ContainsDirectoriesAndFiles()
    {
        var stats = new TreeStats(3, 10, 49356);
        string json = Formatting.FormatJsonSummary(stats, 0, "success", ToolName, Version);

        Assert.Contains("\"directories\":3", json);
        Assert.Contains("\"files\":10", json);
    }

    [Fact]
    public void FormatJsonSummary_ContainsTotalSizeBytes()
    {
        var stats = new TreeStats(3, 10, 49356);
        string json = Formatting.FormatJsonSummary(stats, 0, "success", ToolName, Version);

        Assert.Contains("\"total_size_bytes\":49356", json);
    }

    [Fact]
    public void FormatJsonSummary_OmitsTotalSizeBytesWhenNegative()
    {
        var stats = new TreeStats(3, 10, -1);
        string json = Formatting.FormatJsonSummary(stats, 0, "success", ToolName, Version);

        Assert.DoesNotContain("total_size_bytes", json);
    }

    [Fact]
    public void FormatJsonSummary_IsValidJson()
    {
        var stats = new TreeStats(3, 10, 49356);
        string json = Formatting.FormatJsonSummary(stats, 0, "success", ToolName, Version);

        JsonDocument.Parse(json);
    }

    [Fact]
    public void FormatJsonSummary_WithoutSize_IsValidJson()
    {
        var stats = new TreeStats(3, 10, -1);
        string json = Formatting.FormatJsonSummary(stats, 0, "success", ToolName, Version);

        JsonDocument.Parse(json);
    }

    // --- FormatJsonError ---

    [Fact]
    public void FormatJsonError_ContainsExitCodeAndReason()
    {
        string json = Formatting.FormatJsonError(125, "usage_error", ToolName, Version);

        Assert.Contains("\"tool\":\"treex\"", json);
        Assert.Contains("\"exit_code\":125", json);
        Assert.Contains("\"exit_reason\":\"usage_error\"", json);
    }

    [Fact]
    public void FormatJsonError_IsValidJson()
    {
        string json = Formatting.FormatJsonError(126, "not_found", ToolName, Version);

        JsonDocument.Parse(json);
    }

    // --- FormatSummaryLine ---

    [Fact]
    public void FormatSummaryLine_BasicFormat()
    {
        var stats = new TreeStats(3, 10, -1);
        string line = Formatting.FormatSummaryLine(stats);

        Assert.Equal("3 directories, 10 files", line);
    }

    [Fact]
    public void FormatSummaryLine_SingularDirectory()
    {
        var stats = new TreeStats(1, 10, -1);
        string line = Formatting.FormatSummaryLine(stats);

        Assert.Equal("1 directory, 10 files", line);
    }

    [Fact]
    public void FormatSummaryLine_SingularFile()
    {
        var stats = new TreeStats(3, 1, -1);
        string line = Formatting.FormatSummaryLine(stats);

        Assert.Equal("3 directories, 1 file", line);
    }

    [Fact]
    public void FormatSummaryLine_WithSize_ShowsTotalInParentheses()
    {
        var stats = new TreeStats(3, 10, 49356);
        string line = Formatting.FormatSummaryLine(stats);

        Assert.StartsWith("3 directories, 10 files (", line);
        Assert.EndsWith(")", line);
        Assert.Contains("48.2K", line);
    }

    [Fact]
    public void FormatSummaryLine_WithZeroSize_ShowsZero()
    {
        var stats = new TreeStats(0, 0, 0);
        string line = Formatting.FormatSummaryLine(stats);

        Assert.Equal("0 directories, 0 files (0)", line);
    }

    [Fact]
    public void FormatSummaryLine_NegativeSize_OmitsParentheses()
    {
        var stats = new TreeStats(3, 10, -1);
        string line = Formatting.FormatSummaryLine(stats);

        Assert.DoesNotContain("(", line);
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
}

public class NdjsonDirsOnlyTests
{
    private static readonly DateTimeOffset FixedDate = new(2026, 3, 31, 14, 22, 0, TimeSpan.FromHours(13));

    /// <summary>
    /// Verifies that a dirs-only NDJSON walk skips file nodes.
    /// This mirrors the filtering logic in Program.WriteNdjsonTree.
    /// </summary>
    [Fact]
    public void DirsOnly_NdjsonWalk_ExcludesFiles()
    {
        var root = new TreeNode
        {
            Name = "root", FullPath = "/tmp/root",
            Type = FileEntryType.Directory, SizeBytes = -1, Modified = FixedDate
        };
        var sub = new TreeNode
        {
            Name = "sub", FullPath = "/tmp/root/sub",
            Type = FileEntryType.Directory, SizeBytes = -1, Modified = FixedDate
        };
        var file1 = new TreeNode
        {
            Name = "a.txt", FullPath = "/tmp/root/a.txt",
            Type = FileEntryType.File, SizeBytes = 100, Modified = FixedDate
        };
        var file2 = new TreeNode
        {
            Name = "b.txt", FullPath = "/tmp/root/sub/b.txt",
            Type = FileEntryType.File, SizeBytes = 200, Modified = FixedDate
        };
        root.Children.Add(sub);
        root.Children.Add(file1);
        sub.Children.Add(file2);

        var collected = new List<string>();
        CollectNdjsonNames(root, dirsOnly: true, collected);

        Assert.Contains("root", collected);
        Assert.Contains("sub", collected);
        Assert.DoesNotContain("a.txt", collected);
        Assert.DoesNotContain("b.txt", collected);
    }

    /// <summary>
    /// Replicates the NDJSON walk with dirs-only filtering to verify the algorithm.
    /// </summary>
    private static void CollectNdjsonNames(TreeNode node, bool dirsOnly, List<string> names)
    {
        names.Add(node.Name);
        foreach (TreeNode child in node.Children)
        {
            if (dirsOnly && child.Type != FileEntryType.Directory)
            {
                continue;
            }

            CollectNdjsonNames(child, dirsOnly, names);
        }
    }
}
