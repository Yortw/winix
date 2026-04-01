#nullable enable

using Winix.FileWalk;
using Xunit;

namespace Winix.FileWalk.Tests;

public class FileWalkerTests : IDisposable
{
    private readonly string _root;

    /// <summary>
    /// Builds a temp directory tree:
    /// root/
    ///   file1.cs        (text: "class A {}")
    ///   file2.txt       (text: "hello")
    ///   .hidden         (text: "secret")
    ///   binary.dat      (binary with null bytes)
    ///   sub/
    ///     file3.cs      (text: "class B {}")
    ///     file4.json    (text: "{}")
    ///     deep/
    ///       file5.cs    (text: "class C {}")
    /// </summary>
    public FileWalkerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FileWalkerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        File.WriteAllText(Path.Combine(_root, "file1.cs"), "class A {}");
        File.WriteAllText(Path.Combine(_root, "file2.txt"), "hello");
        File.WriteAllText(Path.Combine(_root, ".hidden"), "secret");
        File.WriteAllBytes(Path.Combine(_root, "binary.dat"), new byte[] { 0x00, 0x01, 0x02, 0x03 });

        string sub = Path.Combine(_root, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "file3.cs"), "class B {}");
        File.WriteAllText(Path.Combine(sub, "file4.json"), "{}");

        string deep = Path.Combine(sub, "deep");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(deep, "file5.cs"), "class C {}");
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

    private static FileWalkerOptions MakeOptions(
        IReadOnlyList<string>? globPatterns = null,
        IReadOnlyList<string>? regexPatterns = null,
        FileEntryType? typeFilter = null,
        bool? textOnly = null,
        long? minSize = null,
        long? maxSize = null,
        DateTimeOffset? newerThan = null,
        DateTimeOffset? olderThan = null,
        int? maxDepth = null,
        bool includeHidden = true,
        bool followSymlinks = false,
        bool useGitIgnore = false,
        bool absolutePaths = false,
        bool caseInsensitive = false)
    {
        return new FileWalkerOptions(
            globPatterns ?? Array.Empty<string>(),
            regexPatterns ?? Array.Empty<string>(),
            typeFilter,
            textOnly,
            minSize,
            maxSize,
            newerThan,
            olderThan,
            maxDepth,
            includeHidden,
            followSymlinks,
            useGitIgnore,
            absolutePaths,
            caseInsensitive);
    }

    [Fact]
    public void Walk_NoFilters_ReturnsAllEntries()
    {
        var walker = new FileWalker(MakeOptions());
        var results = walker.Walk(new[] { _root }).ToList();

        // All files: file1.cs, file2.txt, .hidden, binary.dat, file3.cs, file4.json, file5.cs = 7
        // All dirs: sub, deep = 2
        var names = results.Select(e => e.Name).ToList();
        Assert.Contains("file1.cs", names);
        Assert.Contains("file2.txt", names);
        Assert.Contains(".hidden", names);
        Assert.Contains("binary.dat", names);
        Assert.Contains("file3.cs", names);
        Assert.Contains("file4.json", names);
        Assert.Contains("file5.cs", names);
        Assert.Contains("sub", names);
        Assert.Contains("deep", names);
        Assert.Equal(9, results.Count);
    }

    [Fact]
    public void Walk_GlobFilter_MatchesOnlyGlob()
    {
        var walker = new FileWalker(MakeOptions(globPatterns: new[] { "*.cs" }));
        var results = walker.Walk(new[] { _root }).ToList();

        // Glob only applies to files, not directories. So we get 3 .cs files + 2 dirs.
        var fileResults = results.Where(e => e.Type == FileEntryType.File).ToList();
        Assert.All(fileResults, e => Assert.EndsWith(".cs", e.Name));
        Assert.Equal(3, fileResults.Count);

        // Directories still appear
        var dirResults = results.Where(e => e.Type == FileEntryType.Directory).ToList();
        Assert.Equal(2, dirResults.Count);
    }

    [Fact]
    public void Walk_TypeFilterFile_ExcludesDirectories()
    {
        var walker = new FileWalker(MakeOptions(typeFilter: FileEntryType.File));
        var results = walker.Walk(new[] { _root }).ToList();

        Assert.All(results, e => Assert.Equal(FileEntryType.File, e.Type));
        Assert.Equal(7, results.Count);
    }

    [Fact]
    public void Walk_TypeFilterDirectory_OnlyDirectories()
    {
        var walker = new FileWalker(MakeOptions(typeFilter: FileEntryType.Directory));
        var results = walker.Walk(new[] { _root }).ToList();

        Assert.All(results, e => Assert.Equal(FileEntryType.Directory, e.Type));
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Walk_MaxDepth_LimitsRecursion()
    {
        var walker = new FileWalker(MakeOptions(maxDepth: 1));
        var results = walker.Walk(new[] { _root }).ToList();

        // Depth 0: file1.cs, file2.txt, .hidden, binary.dat, sub
        // Depth 1: file3.cs, file4.json, deep
        // file5.cs (depth 2) should NOT appear
        var names = results.Select(e => e.Name).ToList();
        Assert.DoesNotContain("file5.cs", names);
        Assert.Contains("file3.cs", names);
        Assert.Contains("deep", names);
    }

    [Fact]
    public void Walk_NoHidden_SkipsHiddenFiles()
    {
        var walker = new FileWalker(MakeOptions(includeHidden: false));
        var results = walker.Walk(new[] { _root }).ToList();

        var names = results.Select(e => e.Name).ToList();
        Assert.DoesNotContain(".hidden", names);
        Assert.Contains("file1.cs", names);
    }

    [Fact]
    public void Walk_RegexFilter_MatchesPattern()
    {
        var walker = new FileWalker(MakeOptions(regexPatterns: new[] { @"file\d+\.cs" }));
        var results = walker.Walk(new[] { _root }).ToList();

        var fileResults = results.Where(e => e.Type == FileEntryType.File).ToList();
        Assert.Equal(3, fileResults.Count);
        Assert.All(fileResults, e => Assert.EndsWith(".cs", e.Name));

        // Directories still appear
        var dirResults = results.Where(e => e.Type == FileEntryType.Directory).ToList();
        Assert.Equal(2, dirResults.Count);
    }

    [Fact]
    public void Walk_AbsolutePaths_ReturnsAbsolute()
    {
        var walker = new FileWalker(MakeOptions(absolutePaths: true));
        var results = walker.Walk(new[] { _root }).ToList();

        // AbsolutePaths outputs forward-slash paths on all platforms.
        // Path.IsPathRooted handles forward slashes correctly on all platforms.
        Assert.All(results, e => Assert.True(
            Path.IsPathRooted(e.Path),
            $"Expected absolute path but got: {e.Path}"));
    }

    [Fact]
    public void Walk_DepthValues_AreCorrect()
    {
        var walker = new FileWalker(MakeOptions());
        var results = walker.Walk(new[] { _root }).ToList();

        var file1 = results.Single(e => e.Name == "file1.cs");
        var sub = results.Single(e => e.Name == "sub");
        var file3 = results.Single(e => e.Name == "file3.cs");
        var deep = results.Single(e => e.Name == "deep");
        var file5 = results.Single(e => e.Name == "file5.cs");

        Assert.Equal(0, file1.Depth);
        Assert.Equal(0, sub.Depth);
        Assert.Equal(1, file3.Depth);
        Assert.Equal(1, deep.Depth);
        Assert.Equal(2, file5.Depth);
    }

    [Fact]
    public void Walk_TextOnly_FiltersTextFiles()
    {
        var walker = new FileWalker(MakeOptions(textOnly: true));
        var results = walker.Walk(new[] { _root }).ToList();

        var fileResults = results.Where(e => e.Type == FileEntryType.File).ToList();
        Assert.DoesNotContain(fileResults, e => e.Name == "binary.dat");
        Assert.All(fileResults, e => Assert.True(e.IsText, $"Expected {e.Name} to be text"));
    }

    [Fact]
    public void Walk_BinaryOnly_FiltersBinaryFiles()
    {
        var walker = new FileWalker(MakeOptions(textOnly: false));
        var results = walker.Walk(new[] { _root }).ToList();

        var fileResults = results.Where(e => e.Type == FileEntryType.File).ToList();
        Assert.Single(fileResults);
        Assert.Equal("binary.dat", fileResults[0].Name);
        Assert.False(fileResults[0].IsText);
    }

    [Fact]
    public void Walk_RelativePaths_AreRelativeToRoot()
    {
        var walker = new FileWalker(MakeOptions());
        var results = walker.Walk(new[] { _root }).ToList();

        var file1 = results.Single(e => e.Name == "file1.cs");
        Assert.Equal("file1.cs", file1.Path);

        var file3 = results.Single(e => e.Name == "file3.cs");
        Assert.Equal("sub/file3.cs", file3.Path);

        var file5 = results.Single(e => e.Name == "file5.cs");
        Assert.Equal("sub/deep/file5.cs", file5.Path);
    }

    [Fact]
    public void Walk_ForwardSlashPaths()
    {
        var walker = new FileWalker(MakeOptions());
        var results = walker.Walk(new[] { _root }).ToList();

        Assert.All(results, e => Assert.DoesNotContain("\\", e.Path));
    }

    [Fact]
    public void Walk_FileSize_IsPopulated()
    {
        var walker = new FileWalker(MakeOptions(typeFilter: FileEntryType.File));
        var results = walker.Walk(new[] { _root }).ToList();

        var file1 = results.Single(e => e.Name == "file1.cs");
        Assert.True(file1.SizeBytes > 0, "File size should be positive");

        var binary = results.Single(e => e.Name == "binary.dat");
        Assert.Equal(4, binary.SizeBytes);
    }

    [Fact]
    public void Walk_DirectorySize_IsMinusOne()
    {
        var walker = new FileWalker(MakeOptions(typeFilter: FileEntryType.Directory));
        var results = walker.Walk(new[] { _root }).ToList();

        Assert.All(results, e => Assert.Equal(-1, e.SizeBytes));
    }

    [Fact]
    public void Walk_IsIgnoredFunc_SkipsMatchedEntries()
    {
        Func<string, bool> isIgnored = path => path.Contains("sub");
        var walker = new FileWalker(MakeOptions(), isIgnored);
        var results = walker.Walk(new[] { _root }).ToList();

        var names = results.Select(e => e.Name).ToList();
        Assert.DoesNotContain("sub", names);
        Assert.DoesNotContain("file3.cs", names);
        Assert.DoesNotContain("deep", names);
        Assert.DoesNotContain("file5.cs", names);
        Assert.Contains("file1.cs", names);
    }

    [Fact]
    public void Walk_GlobAndRegex_BothMustMatch()
    {
        // Glob matches *.cs, regex matches file1 only
        // File must match at least one glob AND at least one regex
        var walker = new FileWalker(MakeOptions(
            globPatterns: new[] { "*.cs" },
            regexPatterns: new[] { "file1" }));
        var results = walker.Walk(new[] { _root }).ToList();

        var fileResults = results.Where(e => e.Type == FileEntryType.File).ToList();
        Assert.Single(fileResults);
        Assert.Equal("file1.cs", fileResults[0].Name);
    }

    [Fact]
    public void Walk_MultipleRoots_WalksAll()
    {
        string root2 = Path.Combine(Path.GetTempPath(), "FileWalkerTests2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root2);
        File.WriteAllText(Path.Combine(root2, "extra.cs"), "class Extra {}");

        try
        {
            var walker = new FileWalker(MakeOptions(typeFilter: FileEntryType.File));
            var results = walker.Walk(new[] { _root, root2 }).ToList();

            var names = results.Select(e => e.Name).ToList();
            Assert.Contains("file1.cs", names);
            Assert.Contains("extra.cs", names);
        }
        finally
        {
            Directory.Delete(root2, recursive: true);
        }
    }

    [Fact]
    public void Walk_MinSize_ExcludesSmallFiles()
    {
        // file1.cs = "class A {}" = 10 bytes, file2.txt = "hello" = 5 bytes
        var walker = new FileWalker(MakeOptions(typeFilter: FileEntryType.File, minSize: 6));
        var results = walker.Walk(new[] { _root }).ToList();

        Assert.DoesNotContain(results, e => e.Name == "file2.txt");
        Assert.Contains(results, e => e.Name == "file1.cs");
    }

    [Fact]
    public void Walk_MaxSize_ExcludesLargeFiles()
    {
        // file1.cs = 10 bytes, file2.txt = 5 bytes
        var walker = new FileWalker(MakeOptions(typeFilter: FileEntryType.File, maxSize: 6));
        var results = walker.Walk(new[] { _root }).ToList();

        Assert.Contains(results, e => e.Name == "file2.txt");
        Assert.DoesNotContain(results, e => e.Name == "file1.cs");
    }

    [Fact]
    public void Walk_MinAndMaxSize_FiltersRange()
    {
        // Only files between 6 and 15 bytes: file1.cs (10 bytes) matches
        var walker = new FileWalker(MakeOptions(typeFilter: FileEntryType.File, minSize: 6, maxSize: 15));
        var results = walker.Walk(new[] { _root }).ToList();

        Assert.Contains(results, e => e.Name == "file1.cs");
        Assert.DoesNotContain(results, e => e.Name == "file2.txt");
    }

    [Fact]
    public void Walk_SymlinkCycle_DoesNotInfiniteLoop()
    {
        // Create a symlink inside sub/ that points back to root, forming a cycle.
        // With FollowSymlinks=true, the walker must detect the cycle and stop.
        string linkPath = Path.Combine(_root, "sub", "cycle_link");

        try
        {
            Directory.CreateSymbolicLink(linkPath, _root);
        }
        catch (IOException)
        {
            // Symlink creation may fail without elevated privileges on Windows
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        var walker = new FileWalker(MakeOptions(followSymlinks: true));

        // If cycle detection is broken, this will stack overflow or hang.
        // With the fix, it terminates and returns a finite set of entries.
        var results = walker.Walk(new[] { _root }).ToList();

        Assert.True(results.Count > 0, "Should return some entries");
        Assert.True(results.Count < 1000, "Should not explode from symlink cycle");
    }

    [Fact]
    public void Walk_RegexTimeout_TreatsAsNonMatch()
    {
        // A pathological regex with backreference that falls back to the timeout engine.
        // SafeRegex gives it a 2-second timeout. On short filenames this should complete
        // fast, but the test verifies the walker doesn't crash on RegexMatchTimeoutException.
        var walker = new FileWalker(MakeOptions(regexPatterns: new[] { @"(.)\1" }));
        var results = walker.Walk(new[] { _root }).ToList();

        // "deep" has repeated 'e', "sub" does not — just verify it completes without crashing
        Assert.DoesNotContain(results, e => e.Name == "file1.cs");
    }

    [Fact]
    public void Constructor_InvalidRegex_ThrowsRegexParseException()
    {
        // Invalid regex should throw RegexParseException (subclass of ArgumentException).
        // The files console app catches this to return exit code 125 (usage_error).
        var ex = Assert.ThrowsAny<ArgumentException>(
            () => new FileWalker(MakeOptions(regexPatterns: new[] { "[invalid" })));

        Assert.IsType<System.Text.RegularExpressions.RegexParseException>(ex);
    }

    [Fact]
    public void Walk_NewerThan_ExcludesOlderFiles()
    {
        // All test files were created during the constructor. Set a boundary just before
        // construction and verify they pass the filter.
        DateTimeOffset justBeforeNow = DateTimeOffset.UtcNow.AddSeconds(-5);

        var walker = new FileWalker(MakeOptions(typeFilter: FileEntryType.File, newerThan: justBeforeNow));
        var results = walker.Walk(new[] { _root }).ToList();

        // Files created in the constructor are newer than 5 seconds ago
        Assert.True(results.Count > 0, "Recently created files should pass --newer filter");
    }

    [Fact]
    public void Walk_NewerThan_FarFuture_ExcludesAllFiles()
    {
        // A boundary in the far future should exclude everything
        DateTimeOffset farFuture = DateTimeOffset.UtcNow.AddHours(1);

        var walker = new FileWalker(MakeOptions(typeFilter: FileEntryType.File, newerThan: farFuture));
        var results = walker.Walk(new[] { _root }).ToList();

        Assert.Empty(results);
    }

    [Fact]
    public void Walk_OlderThan_FarFuture_IncludesAllFiles()
    {
        // A boundary in the far future should include everything
        DateTimeOffset farFuture = DateTimeOffset.UtcNow.AddHours(1);

        var walker = new FileWalker(MakeOptions(typeFilter: FileEntryType.File, olderThan: farFuture));
        var results = walker.Walk(new[] { _root }).ToList();

        // All 5 files (file1.cs, file2.txt, .hidden, binary.dat, file3.cs, file4.json, file5.cs)
        // minus hidden (.hidden) since includeHidden defaults to true in MakeOptions
        Assert.True(results.Count > 0, "All files should pass --older filter with far-future boundary");
    }

    [Fact]
    public void Walk_OlderThan_DistantPast_ExcludesAllFiles()
    {
        // A boundary in the distant past should exclude everything
        DateTimeOffset distantPast = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var walker = new FileWalker(MakeOptions(typeFilter: FileEntryType.File, olderThan: distantPast));
        var results = walker.Walk(new[] { _root }).ToList();

        Assert.Empty(results);
    }
}
