using System.Diagnostics;
using Xunit;
using Yort.ShellKit;

namespace Yort.ShellKit.Tests;

public class GitIgnoreFilterTests : IDisposable
{
    private readonly string _tempDir;

    public GitIgnoreFilterTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "winix-gitignore-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void Create_NotGitRepo_ReturnsNull()
    {
        using GitIgnoreFilter? filter = GitIgnoreFilter.Create(_tempDir);

        Assert.Null(filter);
    }

    [Fact]
    public void Create_GitRepoWithIgnore_ReturnsFilter()
    {
        InitGitRepo();
        File.WriteAllText(Path.Combine(_tempDir, ".gitignore"), "*.log\nbin/\n");

        using GitIgnoreFilter? filter = GitIgnoreFilter.Create(_tempDir);

        Assert.NotNull(filter);
    }

    [Fact]
    public void IsIgnored_IgnoredFile_ReturnsTrue()
    {
        InitGitRepo();
        File.WriteAllText(Path.Combine(_tempDir, ".gitignore"), "*.log\n");
        File.WriteAllText(Path.Combine(_tempDir, "debug.log"), "log content");

        using GitIgnoreFilter? filter = GitIgnoreFilter.Create(_tempDir);
        Assert.NotNull(filter);

        Assert.True(filter!.IsIgnored("debug.log"));
    }

    [Fact]
    public void IsIgnored_NotIgnoredFile_ReturnsFalse()
    {
        InitGitRepo();
        File.WriteAllText(Path.Combine(_tempDir, ".gitignore"), "*.log\n");
        File.WriteAllText(Path.Combine(_tempDir, "readme.md"), "hello");

        using GitIgnoreFilter? filter = GitIgnoreFilter.Create(_tempDir);
        Assert.NotNull(filter);

        Assert.False(filter!.IsIgnored("readme.md"));
    }

    [Fact]
    public void IsIgnored_IgnoredDirectory_ReturnsTrue()
    {
        InitGitRepo();
        File.WriteAllText(Path.Combine(_tempDir, ".gitignore"), "bin/\n");
        Directory.CreateDirectory(Path.Combine(_tempDir, "bin"));

        using GitIgnoreFilter? filter = GitIgnoreFilter.Create(_tempDir);
        Assert.NotNull(filter);

        Assert.True(filter!.IsIgnored("bin/"));
    }

    [Fact]
    public void IsIgnored_MultiplePathsInSingleSession_AllCorrect()
    {
        InitGitRepo();
        File.WriteAllText(Path.Combine(_tempDir, ".gitignore"), "*.log\nbin/\n");
        File.WriteAllText(Path.Combine(_tempDir, "debug.log"), "log content");
        File.WriteAllText(Path.Combine(_tempDir, "readme.md"), "hello");
        Directory.CreateDirectory(Path.Combine(_tempDir, "bin"));

        using GitIgnoreFilter? filter = GitIgnoreFilter.Create(_tempDir);
        Assert.NotNull(filter);

        // Exercise the long-running process with several sequential queries.
        Assert.True(filter!.IsIgnored("debug.log"));
        Assert.False(filter.IsIgnored("readme.md"));
        Assert.True(filter.IsIgnored("bin/"));
        Assert.False(filter.IsIgnored("readme.md"));
        Assert.True(filter.IsIgnored("debug.log"));
    }

    [Fact]
    public void CheckBatch_ReturnsIgnoredPaths()
    {
        InitGitRepo();
        File.WriteAllText(Path.Combine(_tempDir, ".gitignore"), "*.log\nbin/\n");
        File.WriteAllText(Path.Combine(_tempDir, "debug.log"), "log content");
        File.WriteAllText(Path.Combine(_tempDir, "readme.md"), "hello");
        Directory.CreateDirectory(Path.Combine(_tempDir, "bin"));

        using GitIgnoreFilter? filter = GitIgnoreFilter.Create(_tempDir);
        Assert.NotNull(filter);

        HashSet<string> ignored = filter!.CheckBatch(new[] { "debug.log", "readme.md", "src/main.cs" });

        Assert.Contains("debug.log", ignored);
        Assert.DoesNotContain("readme.md", ignored);
        Assert.DoesNotContain("src/main.cs", ignored);
    }

    [Fact]
    public void CheckBatch_EmptyInput_ReturnsEmptySet()
    {
        InitGitRepo();
        File.WriteAllText(Path.Combine(_tempDir, ".gitignore"), "*.log\n");

        using GitIgnoreFilter? filter = GitIgnoreFilter.Create(_tempDir);
        Assert.NotNull(filter);

        HashSet<string> ignored = filter!.CheckBatch(Array.Empty<string>());

        Assert.Empty(ignored);
    }

    [Fact]
    public void CheckBatch_OverChunkSize_ProcessesAllPaths()
    {
        InitGitRepo();
        File.WriteAllText(Path.Combine(_tempDir, ".gitignore"), "ignored-*\n");

        // Create 150 paths (exceeds the 100-path chunk size)
        var paths = new List<string>();
        for (int i = 0; i < 150; i++)
        {
            string name = i % 3 == 0 ? $"ignored-{i}.txt" : $"keep-{i}.txt";
            paths.Add(name);
        }

        using GitIgnoreFilter? filter = GitIgnoreFilter.Create(_tempDir);
        Assert.NotNull(filter);

        HashSet<string> ignored = filter!.CheckBatch(paths);

        // Every third file should be ignored (i % 3 == 0)
        Assert.Equal(50, ignored.Count);
        Assert.Contains("ignored-0.txt", ignored);
        Assert.Contains("ignored-99.txt", ignored);
        Assert.DoesNotContain("keep-1.txt", ignored);
    }

    [Fact]
    public void IsIgnored_AfterDispose_ThrowsObjectDisposedException()
    {
        InitGitRepo();
        File.WriteAllText(Path.Combine(_tempDir, ".gitignore"), "*.log\n");

        GitIgnoreFilter? filter = GitIgnoreFilter.Create(_tempDir);
        Assert.NotNull(filter);
        filter!.Dispose();

        Assert.Throws<ObjectDisposedException>(() => filter.IsIgnored("debug.log"));
    }

    private void InitGitRepo()
    {
        RunGit("init");
        RunGit("config user.email test@test.com");
        RunGit("config user.name Test");
    }

    private void RunGit(string arguments)
    {
        var psi = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = _tempDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi)!;
        process.WaitForExit(10000);
    }
}
