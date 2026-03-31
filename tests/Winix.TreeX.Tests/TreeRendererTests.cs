#nullable enable

using Winix.FileWalk;
using Winix.TreeX;
using Xunit;

namespace Winix.TreeX.Tests;

public class TreeRendererTests
{
    private static readonly DateTimeOffset FixedDate = new(2025, 6, 15, 10, 30, 0, TimeSpan.Zero);

    /// <summary>Creates a directory node with a fixed timestamp.</summary>
    private static TreeNode MakeDir(string name, string? fullPath = null)
    {
        return new TreeNode
        {
            Name = name,
            FullPath = fullPath ?? Path.Combine(Path.GetTempPath(), name),
            Type = FileEntryType.Directory,
            SizeBytes = -1,
            Modified = FixedDate
        };
    }

    /// <summary>Creates a file node with a fixed timestamp.</summary>
    private static TreeNode MakeFile(string name, long size, string? fullPath = null)
    {
        return new TreeNode
        {
            Name = name,
            FullPath = fullPath ?? Path.Combine(Path.GetTempPath(), name),
            Type = FileEntryType.File,
            SizeBytes = size,
            Modified = FixedDate
        };
    }

    /// <summary>Creates a symlink node with a fixed timestamp.</summary>
    private static TreeNode MakeSymlink(string name)
    {
        return new TreeNode
        {
            Name = name,
            FullPath = Path.Combine(Path.GetTempPath(), name),
            Type = FileEntryType.Symlink,
            SizeBytes = 100,
            Modified = FixedDate
        };
    }

    /// <summary>Creates an executable file node.</summary>
    private static TreeNode MakeExecutable(string name, long size)
    {
        return new TreeNode
        {
            Name = name,
            FullPath = Path.Combine(Path.GetTempPath(), name),
            Type = FileEntryType.File,
            SizeBytes = size,
            Modified = FixedDate,
            IsExecutable = true
        };
    }

    /// <summary>
    /// Builds: root/ -> sub/ -> gamma.cs (30), alpha.cs (10), beta.txt (20)
    /// </summary>
    private static TreeNode MakeTree()
    {
        var root = MakeDir("root");
        var sub = MakeDir("sub");
        sub.Children.Add(MakeFile("gamma.cs", 30));
        root.Children.Add(sub);
        root.Children.Add(MakeFile("alpha.cs", 10));
        root.Children.Add(MakeFile("beta.txt", 20));
        return root;
    }

    private static TreeRenderOptions NoColor()
    {
        return new TreeRenderOptions(UseColor: false, UseLinks: false, ShowSize: false, ShowDate: false, DirsOnly: false);
    }

    private static string RenderToString(TreeNode root, TreeRenderOptions opts)
    {
        var sw = new StringWriter();
        new TreeRenderer(opts).Render(root, sw);
        return sw.ToString();
    }

    [Fact]
    public void BasicTree_UsesCorrectConnectors()
    {
        var root = MakeTree();
        string output = RenderToString(root, NoColor());

        // sub is first child (not last) -> ├──
        Assert.Contains("\u251C\u2500\u2500 sub", output);
        // alpha.cs is second (not last) -> ├──
        Assert.Contains("\u251C\u2500\u2500 alpha.cs", output);
        // beta.txt is last -> └──
        Assert.Contains("\u2514\u2500\u2500 beta.txt", output);
    }

    [Fact]
    public void LastChild_UsesElbow()
    {
        var root = MakeDir("root");
        root.Children.Add(MakeFile("only.txt", 5));
        string output = RenderToString(root, NoColor());

        Assert.Contains("\u2514\u2500\u2500 only.txt", output);
        // Should NOT have T-junction
        Assert.DoesNotContain("\u251C", output);
    }

    [Fact]
    public void ReturnsCorrectStats()
    {
        var root = MakeTree();
        var sw = new StringWriter();
        var stats = new TreeRenderer(NoColor()).Render(root, sw);

        // 1 directory (sub), 3 files (gamma.cs, alpha.cs, beta.txt)
        Assert.Equal(1, stats.DirectoryCount);
        Assert.Equal(3, stats.FileCount);
    }

    [Fact]
    public void WithColor_DirectoriesAreBlue()
    {
        var root = MakeTree();
        var opts = new TreeRenderOptions(UseColor: true, UseLinks: false, ShowSize: false, ShowDate: false, DirsOnly: false);
        string output = RenderToString(root, opts);

        // Blue escape: \x1b[34m should wrap directory names
        Assert.Contains("\x1b[34m", output);
    }

    [Fact]
    public void NoColor_NoAnsiCodes()
    {
        var root = MakeTree();
        string output = RenderToString(root, NoColor());

        // ESC character (U+001B) should not appear anywhere in uncoloured output.
        // Use ordinal comparison to avoid culture-sensitive false matches with
        // Unicode box-drawing characters in the tree connectors.
        Assert.False(
            output.Contains('\x001b'),
            "Output should not contain ESC (U+001B) when colour is disabled");
    }

    [Fact]
    public void WithColor_ConnectorsAreDim()
    {
        var root = MakeTree();
        var opts = new TreeRenderOptions(UseColor: true, UseLinks: false, ShowSize: false, ShowDate: false, DirsOnly: false);
        string output = RenderToString(root, opts);

        // Dim escape: \x1b[2m should wrap connectors
        Assert.Contains("\x1b[2m", output);
    }

    [Fact]
    public void WithSize_ShowsSizeColumn()
    {
        var root = MakeTree();
        var opts = new TreeRenderOptions(UseColor: false, UseLinks: false, ShowSize: true, ShowDate: false, DirsOnly: false);
        string output = RenderToString(root, opts);

        // Files show their sizes
        Assert.Contains("30", output);
        Assert.Contains("10", output);
        Assert.Contains("20", output);
    }

    [Fact]
    public void WithLinks_EmitsOsc8()
    {
        var root = MakeTree();
        var opts = new TreeRenderOptions(UseColor: false, UseLinks: true, ShowSize: false, ShowDate: false, DirsOnly: false);
        string output = RenderToString(root, opts);

        // OSC 8 hyperlink opener
        Assert.Contains("\x1b]8;;file://", output);
        // OSC 8 closer
        Assert.Contains("\x1b]8;;\x1b\\", output);
    }

    [Fact]
    public void DirsOnly_SuppressesFiles()
    {
        var root = MakeTree();
        var opts = new TreeRenderOptions(UseColor: false, UseLinks: false, ShowSize: false, ShowDate: false, DirsOnly: true);
        string output = RenderToString(root, opts);

        Assert.Contains("sub", output);
        Assert.DoesNotContain("alpha.cs", output);
        Assert.DoesNotContain("beta.txt", output);
        Assert.DoesNotContain("gamma.cs", output);
    }

    [Fact]
    public void NestedIndentation_UsesBarForNonLastParent()
    {
        // root/ -> sub/ -> gamma.cs, alpha.cs
        // sub is NOT the last child of root, so its children should be indented with │
        var root = MakeDir("root");
        var sub = MakeDir("sub");
        sub.Children.Add(MakeFile("gamma.cs", 30));
        root.Children.Add(sub);
        root.Children.Add(MakeFile("alpha.cs", 10));

        string output = RenderToString(root, NoColor());

        // gamma.cs is under sub (non-last child), so prefix should contain │
        Assert.Contains("\u2502   \u2514\u2500\u2500 gamma.cs", output);
    }

    [Fact]
    public void NestedIndentation_UsesSpaceForLastParent()
    {
        // root/ -> sub/ -> gamma.cs
        // sub IS the last child of root, so its children should be indented with spaces
        var root = MakeDir("root");
        var sub = MakeDir("sub");
        sub.Children.Add(MakeFile("gamma.cs", 30));
        root.Children.Add(sub);

        string output = RenderToString(root, NoColor());

        // gamma.cs is under sub (last child), so prefix should be 4 spaces not │
        Assert.Contains("    \u2514\u2500\u2500 gamma.cs", output);
        // Should NOT have vertical bar in prefix for gamma.cs
        string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string gammaLine = lines.First(l => l.Contains("gamma.cs"));
        Assert.DoesNotContain("\u2502", gammaLine);
    }

    [Fact]
    public void WithColor_SymlinksAreCyan()
    {
        var root = MakeDir("root");
        root.Children.Add(MakeSymlink("link"));
        var opts = new TreeRenderOptions(UseColor: true, UseLinks: false, ShowSize: false, ShowDate: false, DirsOnly: false);
        string output = RenderToString(root, opts);

        // Cyan escape: \x1b[36m for symlinks
        Assert.Contains("\x1b[36m", output);
    }

    [Fact]
    public void WithColor_ExecutablesAreGreen()
    {
        var root = MakeDir("root");
        root.Children.Add(MakeExecutable("run.exe", 500));
        var opts = new TreeRenderOptions(UseColor: true, UseLinks: false, ShowSize: false, ShowDate: false, DirsOnly: false);
        string output = RenderToString(root, opts);

        // Green escape: \x1b[32m for executables
        Assert.Contains("\x1b[32m", output);
    }

    [Fact]
    public void WithDate_ShowsDateColumn()
    {
        var root = MakeTree();
        var opts = new TreeRenderOptions(UseColor: false, UseLinks: false, ShowSize: false, ShowDate: true, DirsOnly: false);
        string output = RenderToString(root, opts);

        // The fixed date should appear in local time format yyyy-MM-dd HH:mm
        string localFormatted = FixedDate.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        Assert.Contains(localFormatted, output);
    }

    [Fact]
    public void RootName_IsFirstLine()
    {
        var root = MakeTree();
        string output = RenderToString(root, NoColor());
        string firstLine = output.Split('\n')[0].TrimEnd('\r');

        Assert.Equal("root", firstLine);
    }

    [Fact]
    public void DirsOnly_StatsCountOnlyDirectories()
    {
        var root = MakeTree();
        var opts = new TreeRenderOptions(UseColor: false, UseLinks: false, ShowSize: false, ShowDate: false, DirsOnly: true);
        var sw = new StringWriter();
        var stats = new TreeRenderer(opts).Render(root, sw);

        Assert.Equal(1, stats.DirectoryCount);
        Assert.Equal(0, stats.FileCount);
    }

    [Fact]
    public void EmptyRoot_ReturnsZeroStats()
    {
        var root = MakeDir("empty");
        var sw = new StringWriter();
        var stats = new TreeRenderer(NoColor()).Render(root, sw);

        Assert.Equal(0, stats.DirectoryCount);
        Assert.Equal(0, stats.FileCount);
    }

    [Fact]
    public void WithSizeAndColor_SizeIsDim()
    {
        var root = MakeDir("root");
        root.Children.Add(MakeFile("big.dat", 2048));
        var opts = new TreeRenderOptions(UseColor: true, UseLinks: false, ShowSize: true, ShowDate: false, DirsOnly: false);
        string output = RenderToString(root, opts);

        // Size column should be wrapped with dim
        // The file line should contain dim code and the size
        Assert.Contains("2.0K", output);
        Assert.Contains("\x1b[2m", output);
    }

    [Fact]
    public void WithLinksAndColor_LinkWrapsInsideColor()
    {
        var root = MakeDir("root");
        root.Children.Add(MakeFile("test.cs", 10));
        var opts = new TreeRenderOptions(UseColor: true, UseLinks: true, ShowSize: false, ShowDate: false, DirsOnly: false);
        string output = RenderToString(root, opts);

        // Both OSC 8 and no colour wrapping for a regular file (no colour on name)
        // but root is a dir and should have blue + link
        Assert.Contains("\x1b]8;;file://", output);
        Assert.Contains("\x1b[34m", output); // blue for root dir name
    }

    [Fact]
    public void Stats_TotalSizeBytes_SumsAllFiles()
    {
        var root = MakeTree();
        var sw = new StringWriter();
        var stats = new TreeRenderer(NoColor()).Render(root, sw);

        // gamma.cs=30 + alpha.cs=10 + beta.txt=20 = 60
        Assert.Equal(60, stats.TotalSizeBytes);
    }
}
