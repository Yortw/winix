#nullable enable

using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Winix.Man;
using Xunit;

namespace Winix.Man.Tests;

public sealed class PageDiscoveryTests : IDisposable
{
    private readonly string _tempDir;

    public PageDiscoveryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mantest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private void CreateManPage(string basePath, string name, int section, bool compressed = false)
    {
        string dir = Path.Combine(basePath, $"man{section}");
        Directory.CreateDirectory(dir);
        string content = $".TH {name.ToUpperInvariant()} {section}";
        if (compressed)
        {
            string filePath = Path.Combine(dir, $"{name}.{section}.gz");
            byte[] raw = Encoding.UTF8.GetBytes(content);
            using var fs = File.Create(filePath);
            using var gz = new GZipStream(fs, CompressionLevel.Optimal);
            gz.Write(raw, 0, raw.Length);
        }
        else
        {
            string filePath = Path.Combine(dir, $"{name}.{section}");
            File.WriteAllText(filePath, content);
        }
    }

    [Fact]
    public void FindPage_ExistingPage_ReturnsPath()
    {
        CreateManPage(_tempDir, "ls", 1);
        var discovery = new PageDiscovery(new[] { _tempDir });

        string? result = discovery.FindPage("ls");

        Assert.NotNull(result);
        Assert.EndsWith(".1", result, StringComparison.Ordinal);
    }

    [Fact]
    public void FindPage_WithSection_SearchesOnlyThatSection()
    {
        CreateManPage(_tempDir, "printf", 1);
        CreateManPage(_tempDir, "printf", 3);
        var discovery = new PageDiscovery(new[] { _tempDir });

        string? result = discovery.FindPage("printf", 3);

        Assert.NotNull(result);
        Assert.Contains("man3", result, StringComparison.Ordinal);
    }

    [Fact]
    public void FindPage_NoSection_PrefersSection1()
    {
        CreateManPage(_tempDir, "printf", 1);
        CreateManPage(_tempDir, "printf", 3);
        var discovery = new PageDiscovery(new[] { _tempDir });

        string? result = discovery.FindPage("printf");

        Assert.NotNull(result);
        Assert.Contains("man1", result, StringComparison.Ordinal);
    }

    [Fact]
    public void FindPage_CompressedPage_Found()
    {
        CreateManPage(_tempDir, "gzip", 1, compressed: true);
        var discovery = new PageDiscovery(new[] { _tempDir });

        string? result = discovery.FindPage("gzip");

        Assert.NotNull(result);
        Assert.EndsWith(".gz", result, StringComparison.Ordinal);
    }

    [Fact]
    public void FindPage_NotFound_ReturnsNull()
    {
        var discovery = new PageDiscovery(new[] { _tempDir });

        string? result = discovery.FindPage("nonexistentpage");

        Assert.Null(result);
    }

    [Fact]
    public void FindPage_MultipleSearchPaths_FirstMatchWins()
    {
        string firstPath = Path.Combine(_tempDir, "first");
        string secondPath = Path.Combine(_tempDir, "second");
        Directory.CreateDirectory(firstPath);
        Directory.CreateDirectory(secondPath);
        CreateManPage(firstPath, "ls", 1);
        CreateManPage(secondPath, "ls", 1);
        var discovery = new PageDiscovery(new[] { firstPath, secondPath });

        string? result = discovery.FindPage("ls");

        Assert.NotNull(result);
        Assert.Contains("first", result, StringComparison.Ordinal);
    }

    [Fact]
    public void GetEffectiveSearchPath_ReturnsAllPaths()
    {
        string pathA = Path.Combine(_tempDir, "a");
        string pathB = Path.Combine(_tempDir, "b");
        Directory.CreateDirectory(pathA);
        Directory.CreateDirectory(pathB);
        var discovery = new PageDiscovery(new[] { pathA, pathB });

        IReadOnlyList<string> result = discovery.GetEffectiveSearchPath();

        Assert.Contains(pathA, result);
        Assert.Contains(pathB, result);
    }

    // Tier-2 baseline 2026-05-07 finding F1: pre-fix, BuildSearchPaths used <exeDir>/man as
    // the bundled-pages base — but every Winix tool csproj uses Link="share\man\man1\<tool>.1"
    // for its bundled man page, producing publish layout <exeDir>/share/man/man1/<tool>.1.
    // The two paths never aligned, so a fresh-published man.exe could not find any bundled
    // page (including its own). These tests pin the corrected path so the contract can't
    // drift again without one of them failing first.

    [Fact]
    public void BuildSearchPaths_BundledEntry_UsesShareManSubdirectoryOfExeDir()
    {
        string exeDir = Path.Combine(_tempDir, "exe");
        string bundled = Path.Combine(exeDir, "share", "man");
        Directory.CreateDirectory(bundled);

        IReadOnlyList<string> paths = PageDiscovery.BuildSearchPaths(exeDir, manpathEnv: null);

        Assert.Contains(bundled, paths);
    }

    [Fact]
    public void BuildSearchPaths_BundledEntry_DoesNotUsePlainManSubdirectory()
    {
        // The pre-F1 code looked at <exeDir>/man (no share/). Create that directory but NOT
        // <exeDir>/share/man — if the discovery still works against the old path it'll show up
        // here.
        string exeDir = Path.Combine(_tempDir, "exe");
        string oldPath = Path.Combine(exeDir, "man");
        Directory.CreateDirectory(oldPath);

        IReadOnlyList<string> paths = PageDiscovery.BuildSearchPaths(exeDir, manpathEnv: null);

        Assert.DoesNotContain(oldPath, paths);
    }
}
