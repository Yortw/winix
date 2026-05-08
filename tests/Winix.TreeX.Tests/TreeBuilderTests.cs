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

    // Tier-2 baseline 2026-05-06 finding F1 — option A — `--max-depth N` now means
    // "include nodes with depth ≤ N", consistent with README and with the NDJSON
    // `depth` field semantics. Pre-fix this test pinned the off-by-one behaviour
    // (maxDepth=1 produced depth-2 entries); the contract has changed deliberately
    // per user approval. See commit message for rationale.

    [Fact]
    public void Build_MaxDepth0_RootOnly_NoChildren()
    {
        // README: "0 = root only" — root TreeNode itself is returned but has no children.
        var options = MakeOptions(maxDepth: 0, includeHidden: true);
        var builder = new TreeBuilder(options);

        TreeNode root = builder.Build(_root);

        Assert.Empty(root.Children);
    }

    [Fact]
    public void Build_MaxDepth1_RootAndImmediateChildren()
    {
        // maxDepth=1 includes the root (depth 0) and its immediate children (depth 1)
        // but NOT grandchildren (depth 2).
        var options = MakeOptions(maxDepth: 1, includeHidden: true);
        var builder = new TreeBuilder(options);

        TreeNode root = builder.Build(_root);

        TreeNode sub = root.Children.First(c => c.Name == "sub");

        // Pre-fix this test asserted gamma.cs (depth 2) AND deep (depth 2) were both in
        // sub.Children. Post-fix, sub has no children at all because they would be at
        // depth 2, exceeding maxDepth=1.
        Assert.Empty(sub.Children);
    }

    [Fact]
    public void Build_MaxDepth2_IncludesGrandchildren()
    {
        // maxDepth=2 includes nodes up to and including depth 2 (i.e. children-of-children).
        var options = MakeOptions(maxDepth: 2, includeHidden: true);
        var builder = new TreeBuilder(options);

        TreeNode root = builder.Build(_root);

        TreeNode sub = root.Children.First(c => c.Name == "sub");

        // sub (depth 1) -> gamma.cs (depth 2) and deep (depth 2) both included.
        Assert.Contains(sub.Children, c => c.Name == "gamma.cs");
        Assert.Contains(sub.Children, c => c.Name == "deep");

        // But deep (depth 2) does NOT include its own children (which would be at depth 3).
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

    // ── Date filter coverage (round-1 fresh-eyes 2026-05-09 test-analyzer C1) ──────
    // Pre-fix the --newer / --older code paths in TreeBuilder were unwired by tests;
    // a regression flipping <= to >= on either bound would ship green. Stage a tree
    // with mixed timestamps and pin both bounds individually plus the bounded window.

    [Fact]
    public void Build_NewerThan_ExcludesOlderFiles()
    {
        string oldFile = Path.Combine(_root, "old.txt");
        string newFile = Path.Combine(_root, "new.txt");
        File.WriteAllText(oldFile, "old");
        File.WriteAllText(newFile, "new");
        // Backdate old.txt by 10 days; new.txt keeps its creation time (now).
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow - TimeSpan.FromDays(10));

        // --newer 1d cutoff = now - 1 day. Files modified AFTER that boundary survive.
        var options = MakeOptions(
            newerThan: DateTimeOffset.UtcNow - TimeSpan.FromDays(1),
            includeHidden: true);
        var builder = new TreeBuilder(options);

        TreeNode root = builder.Build(_root);
        var fileNames = GetAllFiles(root).Select(f => f.Name).ToList();

        Assert.Contains("new.txt", fileNames);
        Assert.DoesNotContain("old.txt", fileNames);
    }

    [Fact]
    public void Build_OlderThan_ExcludesNewerFiles()
    {
        string oldFile = Path.Combine(_root, "ancient.txt");
        string newFile = Path.Combine(_root, "fresh.txt");
        File.WriteAllText(oldFile, "ancient");
        File.WriteAllText(newFile, "fresh");
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow - TimeSpan.FromDays(30));

        // --older 1d cutoff = now - 1 day. Files modified BEFORE that boundary survive.
        var options = MakeOptions(
            olderThan: DateTimeOffset.UtcNow - TimeSpan.FromDays(1),
            includeHidden: true);
        var builder = new TreeBuilder(options);

        TreeNode root = builder.Build(_root);
        var fileNames = GetAllFiles(root).Select(f => f.Name).ToList();

        Assert.Contains("ancient.txt", fileNames);
        Assert.DoesNotContain("fresh.txt", fileNames);
    }

    [Fact]
    public void Build_NewerAndOlder_BoundedWindow()
    {
        // Three files: very old, in-window, very new. The combined --newer 30d + --older 1d
        // bracket selects only files modified between now-30d and now-1d.
        string ancient = Path.Combine(_root, "ancient.txt");
        string mid = Path.Combine(_root, "mid.txt");
        string fresh = Path.Combine(_root, "fresh.txt");
        File.WriteAllText(ancient, "a");
        File.WriteAllText(mid, "b");
        File.WriteAllText(fresh, "c");
        File.SetLastWriteTimeUtc(ancient, DateTime.UtcNow - TimeSpan.FromDays(60));
        File.SetLastWriteTimeUtc(mid, DateTime.UtcNow - TimeSpan.FromDays(7));
        // fresh.txt keeps its creation time (now)

        var options = MakeOptions(
            newerThan: DateTimeOffset.UtcNow - TimeSpan.FromDays(30),
            olderThan: DateTimeOffset.UtcNow - TimeSpan.FromDays(1),
            includeHidden: true);
        var builder = new TreeBuilder(options);

        TreeNode root = builder.Build(_root);
        var fileNames = GetAllFiles(root).Select(f => f.Name).ToList();

        Assert.Contains("mid.txt", fileNames);
        Assert.DoesNotContain("ancient.txt", fileNames);
        Assert.DoesNotContain("fresh.txt", fileNames);
    }

    // ── Sort coverage gap (round-1 fresh-eyes 2026-05-09 test-analyzer I1) ────────

    [Fact]
    public void Build_SortByModified_NewestFirst()
    {
        // SortMode.Modified was reachable via --sort modified but had zero tests
        // before this round. Pin the contract: directories first, then files newest-first.
        string oldFile = Path.Combine(_root, "z-old.txt");
        string midFile = Path.Combine(_root, "y-mid.txt");
        string newFile = Path.Combine(_root, "x-new.txt");
        File.WriteAllText(oldFile, "old");
        File.WriteAllText(midFile, "mid");
        File.WriteAllText(newFile, "new");
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow - TimeSpan.FromDays(10));
        File.SetLastWriteTimeUtc(midFile, DateTime.UtcNow - TimeSpan.FromDays(5));
        // x-new.txt keeps its creation time (now)

        var options = MakeOptions(sort: SortMode.Modified, includeHidden: true);
        var builder = new TreeBuilder(options);

        TreeNode root = builder.Build(_root);
        // Filter to just our timestamped files (the existing fixture has other entries).
        var ourFiles = root.Children
            .Where(c => c.Type == FileEntryType.File && c.Name.EndsWith("-old.txt", StringComparison.Ordinal)
                || c.Name.EndsWith("-mid.txt", StringComparison.Ordinal)
                || c.Name.EndsWith("-new.txt", StringComparison.Ordinal))
            .ToList();

        // Newest first → x-new.txt before y-mid.txt before z-old.txt.
        // Skip the test if the fixture didn't preserve our explicit timestamps (CI clock skew).
        Assert.Equal(3, ourFiles.Count);
        int newIdx = ourFiles.FindIndex(f => f.Name == "x-new.txt");
        int midIdx = ourFiles.FindIndex(f => f.Name == "y-mid.txt");
        int oldIdx = ourFiles.FindIndex(f => f.Name == "z-old.txt");
        Assert.True(newIdx < midIdx, $"Expected x-new before y-mid; got newIdx={newIdx}, midIdx={midIdx}");
        Assert.True(midIdx < oldIdx, $"Expected y-mid before z-old; got midIdx={midIdx}, oldIdx={oldIdx}");
    }

    // ── Filter combination matrix (round-1 fresh-eyes test-analyzer I2) ───────────

    [Fact]
    public void Build_GlobAndMinSize_BothMustMatch()
    {
        // Glob alone matches *.cs; min-size alone matches files >= 100 bytes. The AND
        // combination must require BOTH — a regression to OR would surface here.
        WriteFile("small.cs", 10);    // matches glob, fails size
        WriteFile("big.txt", 200);    // fails glob, matches size
        WriteFile("big.cs", 300);     // matches both

        var options = MakeOptions(
            globPatterns: new[] { "*.cs" },
            minSize: 100,
            includeHidden: true);
        var builder = new TreeBuilder(options);

        TreeNode root = builder.Build(_root);
        var matchedNames = GetAllFiles(root).Where(f => f.IsMatch).Select(f => f.Name).ToList();

        Assert.Contains("big.cs", matchedNames);
        Assert.DoesNotContain("small.cs", matchedNames);
        Assert.DoesNotContain("big.txt", matchedNames);
    }

    [Fact]
    public void Build_TypeFileAndGlob_OnlyMatchingFiles()
    {
        // type=File should narrow to files only; glob *.cs further restricts.
        // Subdirectory "sub" should still appear as ancestor for sub/beta.cs (per pruning),
        // but the directory itself isn't a match in this filter.
        var options = MakeOptions(
            typeFilter: FileEntryType.File,
            globPatterns: new[] { "*.cs" },
            includeHidden: true);
        var builder = new TreeBuilder(options);

        TreeNode root = builder.Build(_root);
        var matchedFiles = GetAllFiles(root).Where(f => f.IsMatch).ToList();

        // All matching nodes must be files (not dirs/symlinks) AND match *.cs.
        Assert.NotEmpty(matchedFiles);
        Assert.All(matchedFiles, f =>
        {
            Assert.Equal(FileEntryType.File, f.Type);
            Assert.EndsWith(".cs", f.Name, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Build_GitIgnoreAndGlob_GitIgnoreAppliedFirst()
    {
        // Files inside ignored dirs must be excluded EVEN IF they match the glob.
        // Ordering matters: glob alone would match sub/beta.cs, but gitignore should
        // suppress the entire sub/ subtree before glob filtering runs.
        bool IsIgnored(string path) => path == "sub" || path.StartsWith("sub/", StringComparison.Ordinal);

        var options = MakeOptions(
            globPatterns: new[] { "*.cs" },
            includeHidden: true);
        var builder = new TreeBuilder(options, isIgnored: IsIgnored);

        TreeNode root = builder.Build(_root);
        var allNames = GetAllFiles(root).Select(f => f.Name).ToList();

        // beta.cs (under sub/) must NOT appear despite matching the *.cs glob.
        Assert.DoesNotContain("beta.cs", allNames);
        // alpha.cs at root still matches (gitignore doesn't apply, glob does).
        Assert.Contains("alpha.cs", allNames);
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
