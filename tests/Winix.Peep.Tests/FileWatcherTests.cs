using Xunit;
using Winix.Peep;

namespace Winix.Peep.Tests;

public class FileWatcherExtractRootTests
{
    [Theory]
    [InlineData("src/**/*.cs", "src")]
    [InlineData("**/*.cs", ".")]
    [InlineData("*.cs", ".")]
    [InlineData("src/app/**/*.cs", "src/app")]
    [InlineData("tests/unit/*.cs", "tests/unit")]
    public void ExtractRoot_ReturnsCorrectRoot(string pattern, string expectedRoot)
    {
        string result = FileWatcher.ExtractRoot(pattern);

        // Normalise separators for comparison
        string normalised = expectedRoot.Replace('/', Path.DirectorySeparatorChar);
        Assert.Equal(normalised, result);
    }
}

/// <summary>
/// Integration tests for FileWatcher. These tests change the process-wide current directory
/// so they must not run in parallel with each other.
/// </summary>
[Collection("FileWatcherIntegration")]
public class FileWatcherIntegrationTests
{
    [Fact]
    public async Task FileChanged_FiresOnFileCreate()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"peep-test-{Guid.NewGuid():N}");
        string subDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(subDir);

        string originalDir = Directory.GetCurrentDirectory();
        FileWatcher? watcher = null;
        try
        {
            Directory.SetCurrentDirectory(tempDir);

            watcher = new FileWatcher(new[] { "src/**/*.txt" }, debounceMs: 50);
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            watcher.FileChanged += () => tcs.TrySetResult();
            watcher.Start();

            // Create a file that matches the glob
            string filePath = Path.Combine(subDir, "test.txt");
            await File.WriteAllTextAsync(filePath, "hello");

            // Wait for the debounced event (with timeout)
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
            Assert.Same(tcs.Task, completed);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
            watcher?.Dispose();
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task FileChanged_FiresOnFileModify()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"peep-test-{Guid.NewGuid():N}");
        string subDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(subDir);

        string originalDir = Directory.GetCurrentDirectory();
        FileWatcher? watcher = null;
        try
        {
            Directory.SetCurrentDirectory(tempDir);

            // Pre-create the file
            string filePath = Path.Combine(subDir, "test.txt");
            await File.WriteAllTextAsync(filePath, "original");

            watcher = new FileWatcher(new[] { "src/**/*.txt" }, debounceMs: 50);
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            watcher.FileChanged += () => tcs.TrySetResult();
            watcher.Start();

            // Modify the file
            await Task.Delay(100); // Brief delay to ensure watcher is ready
            await File.WriteAllTextAsync(filePath, "modified");

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
            Assert.Same(tcs.Task, completed);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
            watcher?.Dispose();
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task FileChanged_DoesNotFireForNonMatchingFile()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"peep-test-{Guid.NewGuid():N}");
        string subDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(subDir);

        string originalDir = Directory.GetCurrentDirectory();
        FileWatcher? watcher = null;
        try
        {
            Directory.SetCurrentDirectory(tempDir);

            watcher = new FileWatcher(new[] { "src/**/*.cs" }, debounceMs: 50);
            bool fired = false;
            watcher.FileChanged += () => fired = true;
            watcher.Start();

            // Create a .txt file -- does NOT match *.cs
            string filePath = Path.Combine(subDir, "test.txt");
            await File.WriteAllTextAsync(filePath, "hello");

            // Wait enough time for debounce to have fired if it was going to
            await Task.Delay(500);

            Assert.False(fired);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
            watcher?.Dispose();
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task RapidChanges_DebounceIntoSingleTrigger()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"peep-test-{Guid.NewGuid():N}");
        string subDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(subDir);

        string originalDir = Directory.GetCurrentDirectory();
        FileWatcher? watcher = null;
        try
        {
            Directory.SetCurrentDirectory(tempDir);

            watcher = new FileWatcher(new[] { "src/**/*.txt" }, debounceMs: 200);
            int fireCount = 0;
            watcher.FileChanged += () => Interlocked.Increment(ref fireCount);
            watcher.Start();

            // Create multiple files in rapid succession
            for (int i = 0; i < 5; i++)
            {
                string filePath = Path.Combine(subDir, $"test{i}.txt");
                await File.WriteAllTextAsync(filePath, $"content {i}");
                await Task.Delay(20); // 20ms between writes, well within 200ms debounce
            }

            // Wait for debounce to settle + some buffer
            await Task.Delay(600);

            Assert.Equal(1, fireCount);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
            watcher?.Dispose();
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task MultiplePatterns_MatchIndependently()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"peep-test-{Guid.NewGuid():N}");
        string srcDir = Path.Combine(tempDir, "src");
        string testsDir = Path.Combine(tempDir, "tests");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(testsDir);

        string originalDir = Directory.GetCurrentDirectory();
        FileWatcher? watcher = null;
        try
        {
            Directory.SetCurrentDirectory(tempDir);

            watcher = new FileWatcher(new[] { "src/**/*.txt", "tests/**/*.txt" }, debounceMs: 50);
            int fireCount = 0;
            watcher.FileChanged += () => Interlocked.Increment(ref fireCount);
            watcher.Start();

            // Create a file in src
            await File.WriteAllTextAsync(Path.Combine(srcDir, "a.txt"), "hello");
            await Task.Delay(300); // Wait for debounce

            // Create a file in tests
            await File.WriteAllTextAsync(Path.Combine(testsDir, "b.txt"), "world");
            await Task.Delay(300); // Wait for debounce

            Assert.Equal(2, fireCount);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
            watcher?.Dispose();
            TryDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// Best-effort temp directory cleanup. On Windows, FileSystemWatcher can hold directory
    /// handles briefly after Dispose, causing IOException on immediate delete. Retry with
    /// increasing pauses to let the OS release handles.
    /// </summary>
    private static void TryDeleteDirectory(string path)
    {
        for (int i = 0; i < 10; i++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (i < 9)
            {
                Thread.Sleep(200);
            }
            catch (UnauthorizedAccessException) when (i < 9)
            {
                Thread.Sleep(200);
            }
        }
    }
}
