#nullable enable

using Winix.FileWalk;
using Winix.TreeX;
using Xunit;

namespace Winix.TreeX.Tests;

public class TreeBuilderTests : IDisposable
{
    private readonly string _root;

    /// <summary>
    /// Creates a temp directory tree for testing:
    /// <code>
    /// root/
    ///   .hidden          (1 byte)
    ///   alpha.cs         (10 bytes)
    ///   beta.txt         (20 bytes)
    ///   sub/
    ///     gamma.cs       (30 bytes)
    ///     deep/
    ///       delta.cs     (40 bytes)
    ///   empty/
    /// </code>
    /// </summary>
    public TreeBuilderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "treex_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        WriteFile(".hidden", 1);
        WriteFile("alpha.cs", 10);
        WriteFile("beta.txt", 20);

        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        WriteFile(Path.Combine("sub", "gamma.cs"), 30);

        Directory.CreateDirectory(Path.Combine(_root, "sub", "deep"));
        WriteFile(Path.Combine("sub", "deep", "delta.cs"), 40);

        Directory.CreateDirectory(Path.Combine(_root, "empty"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    [Fact]
    public void Build_NoFilters_ReturnsFullTree()
    {
        var options = MakeOptions(includeHidden: true);
        var builder = new TreeBuilder(options);

        TreeNode root = builder.Build(_root);

        Assert.Equal(Path.GetFileName(_root), root.Name);
        Assert.Equal(FileEntryType.Directory, root.Type);

        // Root should have: .hidden, alpha.cs, beta.txt, sub/, empty/
        var childNames = root.Children.Select(c => c.Name).ToList();
        Assert.Contains(".hidden", childNames);
        Assert.Contains("alpha.cs", childNames);
        Assert.Contains("beta.txt", childNames);
        Assert.Contains("sub", childNames);
        Assert.Contains("empty", childNames);
        Assert.Equal(5, root.Children.Count);

        // sub/ should have gamma.cs and deep/
        TreeNode sub = root.Children.First(c => c.Name == "sub");
        Assert.Equal(2, sub.Children.Count);

        // deep/ should have delta.cs
        TreeNode deep = sub.Children.First(c => c.Name == "deep");
        Assert.Single(deep.Children);
        Assert.Equal("delta.cs", deep.Children[0].Name);
    }

    [Fact]
    public void Build_SortsDirsFirstThenFiles()
    {
        var options = MakeOptions(includeHidden: true);
        var builder = new TreeBuilder(options);

        TreeNode root = builder.Build(_root);

        // Directories should come before files.
        // Dirs: empty, sub. Files: .hidden, alpha.cs, beta.txt
        int lastDirIndex = -1;
        int firstFileIndex = int.MaxValue;
        for (int i = 0; i < root.Children.Count; i++)
        {
            if (root.Children[i].Type == FileEntryType.Directory)
            {
                lastDirIndex = i;
            }
            else
            {
                if (i < firstFileIndex)
                {
                    firstFileIndex = i;
                }
            }
        }

        Assert.True(lastDirIndex < firstFileIndex, "Directories should be sorted before files.");
    }

    [Fact]
    public void Build_GlobFilter_PrunesEmptyBranches()
    {
        var options = MakeOptions(globPatterns: new[] { "*.cs" });
        var builder = new TreeBuilder(options);

        TreeNode root = builder.Build(_root);

        var childNames = root.Children.Select(c => c.Name).ToList();

        // alpha.cs should be present
        Assert.Contains("alpha.cs", childNames);

        // beta.txt should be pruned
        Assert.DoesNotContain("beta.txt", childNames);

        // empty/ should be pruned (no matching descendants)
        Assert.DoesNotContain("empty", childNames);

        // sub/ should survive (has gamma.cs and deep/delta.cs)
        Assert.Contains("sub", childNames);

        TreeNode sub = root.Children.First(c => c.Name == "sub");
        Assert.Contains(sub.Children, c => c.Name == "gamma.cs");
        Assert.Contains(sub.Children, c => c.Name == "deep");
    }

    [Fact]
    public void Build_MaxDepth_LimitsRecursion()
    {
        var options = MakeOptions(maxDepth: 1, includeHidden: true);
        var builder = new TreeBuilder(options);

        TreeNode root = builder.Build(_root);

        // sub/ should be present but its children should not include deep/ content
        TreeNode sub = root.Children.First(c => c.Name == "sub");
        Assert.Contains(sub.Children, c => c.Name == "gamma.cs");
        Assert.Contains(sub.Children, c => c.Name == "deep");

        // deep/ should have NO children (depth limit prevents recursion into depth=2)
        TreeNode deep = sub.Children.First(c => c.Name == "deep");
        Assert.Empty(deep.Children);
    }

    [Fact]
    public void Build_NoHidden_SkipsHiddenFiles()
    {
        var options = MakeOptions(includeHidden: false);
        var builder = new TreeBuilder(options);

        TreeNode root = builder.Build(_root);

        var childNames = root.Children.Select(c => c.Name).ToList();
        Assert.DoesNotContain(".hidden", childNames);
    }

    [Fact]
    public void Build_ComputeSizes_RollsUpDirectorySizes()
    {
        var options = MakeOptions(computeSizes: true, includeHidden: true);
        var builder = new TreeBuilder(options);

        TreeNode root = builder.Build(_root);

        // sub/ should be 30 (gamma.cs) + 40 (delta.cs) = 70
        TreeNode sub = root.Children.First(c => c.Name == "sub");
        Assert.Equal(70, sub.SizeBytes);

        // deep/ should be 40 (delta.cs)
        TreeNode deep = sub.Children.First(c => c.Name == "deep");
        Assert.Equal(40, deep.SizeBytes);

        // root should be 1 (.hidden) + 10 (alpha.cs) + 20 (beta.txt) + 70 (sub/) + 0 (empty/) = 101
        Assert.Equal(101, root.SizeBytes);
    }

    [Fact]
    public void Build_SortBySize_LargestFirst()
    {
        var options = MakeOptions(sort: SortMode.Size, includeHidden: true);
        var builder = new TreeBuilder(options);

        TreeNode root = builder.Build(_root);

        // Get just the file children (skip directories)
        var files = root.Children.Where(c => c.Type == FileEntryType.File).ToList();

        // Files should be sorted largest first: beta.txt (20), alpha.cs (10), .hidden (1)
        Assert.Equal("beta.txt", files[0].Name);
        Assert.Equal("alpha.cs", files[1].Name);
        Assert.Equal(".hidden", files[2].Name);
    }

    [Fact]
    public void Build_FilteredTree_AncestorDirsHaveIsMatchFalse()
    {
        var options = MakeOptions(globPatterns: new[] { "*.cs" });
        var builder = new TreeBuilder(options);

        TreeNode root = builder.Build(_root);

        // sub/ is kept as an ancestor but should have IsMatch = false
        TreeNode sub = root.Children.First(c => c.Name == "sub");
        Assert.False(sub.IsMatch, "Ancestor directory should have IsMatch=false when filters are active.");

        // gamma.cs should have IsMatch = true
        TreeNode gamma = sub.Children.First(c => c.Name == "gamma.cs");
        Assert.True(gamma.IsMatch);

        // alpha.cs at root should have IsMatch = true
        TreeNode alpha = root.Children.First(c => c.Name == "alpha.cs");
        Assert.True(alpha.IsMatch);
    }

    [Fact]
    public void Build_RegexFilter_MatchesAndPrunes()
    {
        var options = MakeOptions(regexPatterns: new[] { @"\.cs$" });
        var builder = new TreeBuilder(options);

        TreeNode root = builder.Build(_root);

        var childNames = root.Children.Select(c => c.Name).ToList();
        Assert.Contains("alpha.cs", childNames);
        Assert.DoesNotContain("beta.txt", childNames);
        Assert.DoesNotContain("empty", childNames);
    }

    [Fact]
    public void Build_GlobAndRegex_RequiresBothToMatch()
    {
        // Glob: *.cs, Regex: gamma — only gamma.cs matches both
        var options = MakeOptions(
            globPatterns: new[] { "*.cs" },
            regexPatterns: new[] { "gamma" });
        var builder = new TreeBuilder(options);

        TreeNode root = builder.Build(_root);

        // alpha.cs matches glob but not regex, should be pruned
        var childNames = root.Children.Select(c => c.Name).ToList();
        Assert.DoesNotContain("alpha.cs", childNames);

        // sub/ should survive with gamma.cs
        Assert.Contains("sub", childNames);
        TreeNode sub = root.Children.First(c => c.Name == "sub");
        Assert.Contains(sub.Children, c => c.Name == "gamma.cs");
    }

    [Fact]
    public void Build_SizeFilter_FiltersByMinAndMax()
    {
        // Only files between 10 and 30 bytes: alpha.cs (10), beta.txt (20), gamma.cs (30)
        var options = MakeOptions(minSize: 10, maxSize: 30);
        var builder = new TreeBuilder(options);

        TreeNode root = builder.Build(_root);

        var allFiles = GetAllFiles(root);
        var fileNames = allFiles.Select(f => f.Name).ToList();

        Assert.Contains("alpha.cs", fileNames);
        Assert.Contains("beta.txt", fileNames);
        Assert.Contains("gamma.cs", fileNames);

        // .hidden (1 byte) and delta.cs (40 bytes) should be excluded
        Assert.DoesNotContain(".hidden", fileNames);
        Assert.DoesNotContain("delta.cs", fileNames);
    }

    [Fact]
    public void Build_TypeFilterDirectory_ShowsOnlyDirs()
    {
        var options = MakeOptions(typeFilter: FileEntryType.Directory, includeHidden: true);
        var builder = new TreeBuilder(options);

        TreeNode root = builder.Build(_root);

        // No files should appear anywhere in the tree
        var allFiles = GetAllFiles(root);
        Assert.Empty(allFiles);

        // Directories should be present
        var childNames = root.Children.Select(c => c.Name).ToList();
        Assert.Contains("sub", childNames);
        Assert.Contains("empty", childNames);
    }

    [Fact]
    public void Build_GitIgnore_SkipsIgnoredPaths()
    {
        // Simulate gitignore via the isIgnored callback
        bool IsIgnored(string path) => path == "sub" || path.StartsWith("sub/");

        var options = MakeOptions(includeHidden: true);
        var builder = new TreeBuilder(options, isIgnored: IsIgnored);

        TreeNode root = builder.Build(_root);

        var childNames = root.Children.Select(c => c.Name).ToList();
        Assert.DoesNotContain("sub", childNames);
        Assert.Contains("alpha.cs", childNames);
    }

    private static TreeBuilderOptions MakeOptions(
        IReadOnlyList<string>? globPatterns = null,
        IReadOnlyList<string>? regexPatterns = null,
        FileEntryType? typeFilter = null,
        long? minSize = null,
        long? maxSize = null,
        DateTimeOffset? newerThan = null,
        DateTimeOffset? olderThan = null,
        int? maxDepth = null,
        bool includeHidden = false,
        bool useGitIgnore = false,
        bool caseInsensitive = false,
        bool computeSizes = false,
        SortMode sort = SortMode.Name)
    {
        return new TreeBuilderOptions(
            GlobPatterns: globPatterns ?? Array.Empty<string>(),
            RegexPatterns: regexPatterns ?? Array.Empty<string>(),
            TypeFilter: typeFilter,
            MinSize: minSize,
            MaxSize: maxSize,
            NewerThan: newerThan,
            OlderThan: olderThan,
            MaxDepth: maxDepth,
            IncludeHidden: includeHidden,
            UseGitIgnore: useGitIgnore,
            CaseInsensitive: caseInsensitive,
            ComputeSizes: computeSizes,
            Sort: sort);
    }

    private void WriteFile(string relativePath, int sizeBytes)
    {
        string fullPath = Path.Combine(_root, relativePath);
        File.WriteAllBytes(fullPath, new byte[sizeBytes]);
    }

    /// <summary>Recursively collects all file nodes from the tree.</summary>
    private static List<TreeNode> GetAllFiles(TreeNode node)
    {
        var files = new List<TreeNode>();
        CollectFiles(node, files);
        return files;
    }

    private static void CollectFiles(TreeNode node, List<TreeNode> files)
    {
        if (node.Type == FileEntryType.File)
        {
            files.Add(node);
        }

        foreach (TreeNode child in node.Children)
        {
            CollectFiles(child, files);
        }
    }
}
