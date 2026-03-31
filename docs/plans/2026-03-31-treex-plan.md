# treex Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the `treex` enhanced directory tree viewer with colour, filtering, size rollups, OSC 8 hyperlinks, and AI discoverability.

**Architecture:** One new class library (`Winix.TreeX`) containing TreeBuilder (recursive walk → tree structure), TreeRenderer (tree → formatted output), and supporting types. Thin console app (`treex`). Reuses `Winix.FileWalk` for predicates (GlobMatcher, SizeParser, DurationParser) and `Yort.ShellKit` for arg parsing, colour, terminal detection, and gitignore.

**Tech Stack:** .NET 10, C#, xUnit. Reuses existing Winix.FileWalk predicates and Yort.ShellKit infrastructure.

**Specs:**
- `docs/plans/2026-03-31-treex-design.md`
- `docs/plans/2026-03-31-treex-adr.md`
- `docs/plans/2026-03-29-winix-cli-conventions.md`

---

## File Map

### New Files

**Winix.TreeX (class library):**
- `src/Winix.TreeX/Winix.TreeX.csproj`
- `src/Winix.TreeX/TreeNode.cs` — in-memory tree node
- `src/Winix.TreeX/TreeBuilderOptions.cs` — config record
- `src/Winix.TreeX/SortMode.cs` — enum
- `src/Winix.TreeX/TreeStats.cs` — result record
- `src/Winix.TreeX/TreeBuilder.cs` — recursive walk, filter, sort, prune, size rollup
- `src/Winix.TreeX/TreeRenderOptions.cs` — rendering config
- `src/Winix.TreeX/TreeRenderer.cs` — tree-line output, colour, links, columns
- `src/Winix.TreeX/HumanSize.cs` — human-readable size formatting (1.2K, 4.8M)
- `src/Winix.TreeX/Formatting.cs` — NDJSON, JSON summary, JSON error

**treex (console app):**
- `src/treex/treex.csproj`
- `src/treex/Program.cs`
- `src/treex/README.md`

**Tests:**
- `tests/Winix.TreeX.Tests/Winix.TreeX.Tests.csproj`
- `tests/Winix.TreeX.Tests/HumanSizeTests.cs`
- `tests/Winix.TreeX.Tests/TreeBuilderTests.cs`
- `tests/Winix.TreeX.Tests/TreeRendererTests.cs`
- `tests/Winix.TreeX.Tests/FormattingTests.cs`

**Docs:**
- `docs/ai/treex.md`

### Modified Files

- `Winix.sln` — add new projects
- `llms.txt` — add treex entry
- `CLAUDE.md` — update project layout
- `README.md` — add treex to shipped table
- `.github/workflows/release.yml` — add treex to pipeline
- `bucket/treex.json` — new scoop manifest
- `bucket/winix.json` — add treex.exe to combined bin

---

## Task 1: Project Scaffolding

**Files:**
- Create: `src/Winix.TreeX/Winix.TreeX.csproj`
- Create: `src/treex/treex.csproj`
- Create: `src/treex/README.md` (minimal placeholder)
- Create: `tests/Winix.TreeX.Tests/Winix.TreeX.Tests.csproj`
- Create placeholder source files
- Modify: `Winix.sln`

- [ ] **Step 1: Create Winix.TreeX class library project**

```xml
<!-- src/Winix.TreeX/Winix.TreeX.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTrimmable>true</IsTrimmable>
    <IsAotCompatible>true</IsAotCompatible>
    <IsPackable>false</IsPackable>
    <PackageId>Winix.TreeX.Library</PackageId>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Winix.FileWalk\Winix.FileWalk.csproj" />
    <ProjectReference Include="..\Yort.ShellKit\Yort.ShellKit.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create treex console app project**

```xml
<!-- src/treex/treex.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
    <OptimizationPreference>Size</OptimizationPreference>
    <StackTraceSupport>false</StackTraceSupport>
    <UseSystemResourceKeys>true</UseSystemResourceKeys>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>treex</ToolCommandName>
    <PackageId>Winix.TreeX</PackageId>
    <Description>Enhanced directory tree with colour, filtering, size rollups, gitignore, and clickable hyperlinks.</Description>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Winix.TreeX\Winix.TreeX.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="/" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create test project**

```xml
<!-- tests/Winix.TreeX.Tests/Winix.TreeX.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Winix.TreeX\Winix.TreeX.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Create placeholder source files**

```csharp
// src/Winix.TreeX/TreeNode.cs
namespace Winix.TreeX;

/// <summary>A node in the directory tree.</summary>
public sealed class TreeNode
{
    /// <summary>Entry name (filename or directory name).</summary>
    public required string Name { get; init; }
}
```

```csharp
// src/treex/Program.cs
namespace TreeX;

internal sealed class Program
{
    static int Main(string[] args)
    {
        return 0;
    }
}
```

```markdown
<!-- src/treex/README.md -->
# treex

Enhanced directory tree viewer. Part of the [Winix](https://github.com/Yortw/winix) tool suite.
```

```csharp
// tests/Winix.TreeX.Tests/HumanSizeTests.cs
namespace Winix.TreeX.Tests;

public class HumanSizeTests
{
}
```

- [ ] **Step 5: Add all projects to solution**

```bash
dotnet sln Winix.sln add src/Winix.TreeX/Winix.TreeX.csproj --solution-folder src
dotnet sln Winix.sln add src/treex/treex.csproj --solution-folder src
dotnet sln Winix.sln add tests/Winix.TreeX.Tests/Winix.TreeX.Tests.csproj --solution-folder tests
```

- [ ] **Step 6: Verify build**

Run: `dotnet build Winix.sln`
Expected: Build succeeded, 0 warnings

- [ ] **Step 7: Commit**

```bash
git add src/Winix.TreeX/ src/treex/ tests/Winix.TreeX.Tests/ Winix.sln
git commit -m "chore: scaffold treex, Winix.TreeX, and test projects"
```

---

## Task 2: HumanSize Formatter

Human-readable size formatting: bytes, K, M, G with one decimal place.

**Files:**
- Create: `src/Winix.TreeX/HumanSize.cs`
- Replace: `tests/Winix.TreeX.Tests/HumanSizeTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/Winix.TreeX.Tests/HumanSizeTests.cs
using Winix.TreeX;
using Xunit;

namespace Winix.TreeX.Tests;

public class HumanSizeTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1")]
    [InlineData(999, "999")]
    [InlineData(1023, "1,023")]
    public void Format_ByteRange_ShowsPlainBytes(long bytes, string expected)
    {
        Assert.Equal(expected, HumanSize.Format(bytes));
    }

    [Theory]
    [InlineData(1024, "1.0K")]
    [InlineData(1536, "1.5K")]
    [InlineData(10240, "10.0K")]
    [InlineData(1048575, "1024.0K")]
    public void Format_KilobyteRange_ShowsK(long bytes, string expected)
    {
        Assert.Equal(expected, HumanSize.Format(bytes));
    }

    [Theory]
    [InlineData(1048576, "1.0M")]
    [InlineData(5242880, "5.0M")]
    [InlineData(1073741823, "1024.0M")]
    public void Format_MegabyteRange_ShowsM(long bytes, string expected)
    {
        Assert.Equal(expected, HumanSize.Format(bytes));
    }

    [Theory]
    [InlineData(1073741824, "1.0G")]
    [InlineData(5368709120, "5.0G")]
    public void Format_GigabyteRange_ShowsG(long bytes, string expected)
    {
        Assert.Equal(expected, HumanSize.Format(bytes));
    }

    [Fact]
    public void Format_NegativeOne_ReturnsDash()
    {
        Assert.Equal("-", HumanSize.Format(-1));
    }

    [Theory]
    [InlineData(0, "0", 5, "    0")]
    [InlineData(1024, "1.0K", 6, "  1.0K")]
    public void FormatPadded_RightAligns(long bytes, string _, int width, string expected)
    {
        Assert.Equal(expected, HumanSize.FormatPadded(bytes, width));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.TreeX.Tests --filter "FullyQualifiedName~HumanSizeTests" -v quiet`
Expected: Build error — `HumanSize` does not exist

- [ ] **Step 3: Implement HumanSize**

```csharp
// src/Winix.TreeX/HumanSize.cs
using System.Globalization;

namespace Winix.TreeX;

/// <summary>
/// Formats byte counts as human-readable sizes: plain bytes below 1024,
/// then K, M, G with one decimal place. Binary units (1K = 1024).
/// </summary>
public static class HumanSize
{
    private const long KB = 1024L;
    private const long MB = KB * 1024;
    private const long GB = MB * 1024;

    /// <summary>Formats a byte count as a human-readable string.</summary>
    public static string Format(long bytes)
    {
        if (bytes < 0)
        {
            return "-";
        }

        if (bytes < KB)
        {
            return bytes.ToString("N0", CultureInfo.InvariantCulture);
        }

        if (bytes < MB)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:F1}K", (double)bytes / KB);
        }

        if (bytes < GB)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:F1}M", (double)bytes / MB);
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:F1}G", (double)bytes / GB);
    }

    /// <summary>Formats a byte count right-aligned to the given width.</summary>
    public static string FormatPadded(long bytes, int width)
    {
        return Format(bytes).PadLeft(width);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Winix.TreeX.Tests --filter "FullyQualifiedName~HumanSizeTests" -v quiet`
Expected: All tests pass

- [ ] **Step 5: Commit**

```bash
git add src/Winix.TreeX/HumanSize.cs tests/Winix.TreeX.Tests/HumanSizeTests.cs
git commit -m "feat(treex): HumanSize formatter for human-readable byte counts"
```

---

## Task 3: Core Types (TreeNode, TreeBuilderOptions, SortMode, TreeStats, TreeRenderOptions)

**Files:**
- Replace: `src/Winix.TreeX/TreeNode.cs`
- Create: `src/Winix.TreeX/TreeBuilderOptions.cs`
- Create: `src/Winix.TreeX/SortMode.cs`
- Create: `src/Winix.TreeX/TreeStats.cs`
- Create: `src/Winix.TreeX/TreeRenderOptions.cs`

- [ ] **Step 1: Define SortMode**

```csharp
// src/Winix.TreeX/SortMode.cs
namespace Winix.TreeX;

/// <summary>Sort order for tree entries.</summary>
public enum SortMode
{
    /// <summary>Alphabetical, directories first (default).</summary>
    Name,

    /// <summary>Largest first, directories first.</summary>
    Size,

    /// <summary>Newest first, directories first.</summary>
    Modified
}
```

- [ ] **Step 2: Define TreeNode**

```csharp
// src/Winix.TreeX/TreeNode.cs
using Winix.FileWalk;

namespace Winix.TreeX;

/// <summary>
/// A node in the in-memory directory tree. Built by <see cref="TreeBuilder"/>,
/// consumed by <see cref="TreeRenderer"/>.
/// </summary>
public sealed class TreeNode
{
    /// <summary>Entry name (filename or directory name).</summary>
    public required string Name { get; init; }

    /// <summary>Full absolute path to this entry.</summary>
    public required string FullPath { get; init; }

    /// <summary>File, directory, or symlink.</summary>
    public required FileEntryType Type { get; init; }

    /// <summary>
    /// File size in bytes. For directories, -1 initially; set to sum of descendants
    /// during size rollup. For files, the actual file size.
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>Last modified timestamp.</summary>
    public DateTimeOffset Modified { get; init; }

    /// <summary>True if the file is executable (Unix permission or Windows extension).</summary>
    public bool IsExecutable { get; init; }

    /// <summary>
    /// True if this entry directly matches the active filters. False for ancestor
    /// directories kept only to show the path to matching descendants.
    /// When no filters are active, all entries have IsMatch = true.
    /// </summary>
    public bool IsMatch { get; set; } = true;

    /// <summary>Child nodes. Empty for files. Sorted by TreeBuilder.</summary>
    public List<TreeNode> Children { get; } = new();
}
```

- [ ] **Step 3: Define TreeBuilderOptions**

```csharp
// src/Winix.TreeX/TreeBuilderOptions.cs
namespace Winix.TreeX;

/// <summary>
/// Immutable configuration for <see cref="TreeBuilder"/>. Controls filtering, sorting,
/// and size computation during tree construction.
/// </summary>
public sealed record TreeBuilderOptions(
    IReadOnlyList<string> GlobPatterns,
    IReadOnlyList<string> RegexPatterns,
    FileWalk.FileEntryType? TypeFilter,
    long? MinSize,
    long? MaxSize,
    DateTimeOffset? NewerThan,
    DateTimeOffset? OlderThan,
    int? MaxDepth,
    bool IncludeHidden,
    bool UseGitIgnore,
    bool CaseInsensitive,
    bool ComputeSizes,
    SortMode Sort);
```

- [ ] **Step 4: Define TreeStats**

```csharp
// src/Winix.TreeX/TreeStats.cs
namespace Winix.TreeX;

/// <summary>Summary statistics from a tree rendering pass.</summary>
/// <param name="DirectoryCount">Number of directories rendered (excluding root).</param>
/// <param name="FileCount">Number of files rendered.</param>
/// <param name="TotalSizeBytes">Total size of all files. -1 if sizes were not computed.</param>
public sealed record TreeStats(int DirectoryCount, int FileCount, long TotalSizeBytes);
```

- [ ] **Step 5: Define TreeRenderOptions**

```csharp
// src/Winix.TreeX/TreeRenderOptions.cs
namespace Winix.TreeX;

/// <summary>Controls how the tree is rendered to text output.</summary>
public sealed record TreeRenderOptions(
    bool UseColor,
    bool UseLinks,
    bool ShowSize,
    bool ShowDate,
    bool DirsOnly);
```

- [ ] **Step 6: Verify build**

Run: `dotnet build src/Winix.TreeX/Winix.TreeX.csproj`
Expected: Build succeeded, 0 warnings

- [ ] **Step 7: Commit**

```bash
git add src/Winix.TreeX/TreeNode.cs src/Winix.TreeX/TreeBuilderOptions.cs src/Winix.TreeX/SortMode.cs src/Winix.TreeX/TreeStats.cs src/Winix.TreeX/TreeRenderOptions.cs
git commit -m "feat(treex): core types — TreeNode, TreeBuilderOptions, SortMode, TreeStats, TreeRenderOptions"
```

---

## Task 4: TreeBuilder

The recursive directory walker that builds the TreeNode hierarchy with filtering, sorting, pruning, and size rollups.

**Files:**
- Create: `src/Winix.TreeX/TreeBuilder.cs`
- Create: `tests/Winix.TreeX.Tests/TreeBuilderTests.cs`

- [ ] **Step 1: Write failing tests**

Tests create a temp directory tree, build a TreeNode, and verify structure.

```csharp
// tests/Winix.TreeX.Tests/TreeBuilderTests.cs
using Winix.FileWalk;
using Winix.TreeX;
using Xunit;

namespace Winix.TreeX.Tests;

public class TreeBuilderTests : IDisposable
{
    private readonly string _root;

    public TreeBuilderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "winix-treex-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);

        // Structure:
        // root/
        //   .hidden
        //   alpha.cs        (10 bytes)
        //   beta.txt        (20 bytes)
        //   sub/
        //     gamma.cs      (30 bytes)
        //     deep/
        //       delta.cs    (40 bytes)
        //   empty/
        CreateFile("alpha.cs", new string('a', 10));
        CreateFile("beta.txt", new string('b', 20));
        CreateFile(".hidden", "x");
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        CreateFile("sub/gamma.cs", new string('c', 30));
        Directory.CreateDirectory(Path.Combine(_root, "sub", "deep"));
        CreateFile("sub/deep/delta.cs", new string('d', 40));
        Directory.CreateDirectory(Path.Combine(_root, "empty"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best effort */ }
    }

    [Fact]
    public void Build_NoFilters_ReturnsFullTree()
    {
        var builder = new TreeBuilder(MakeOptions());
        TreeNode root = builder.Build(_root);

        Assert.Equal(Path.GetFileName(_root), root.Name);
        Assert.True(root.Children.Count >= 4); // .hidden, alpha.cs, beta.txt, sub/, empty/
    }

    [Fact]
    public void Build_SortsDirsFirstThenFiles()
    {
        var builder = new TreeBuilder(MakeOptions(includeHidden: false));
        TreeNode root = builder.Build(_root);

        // Directories should come first
        var names = root.Children.Select(c => c.Name).ToList();
        int firstFileIndex = names.FindIndex(n => !n.Contains("sub") && !n.Contains("empty") && n != "sub" && n != "empty");
        int lastDirIndex = -1;
        for (int i = 0; i < root.Children.Count; i++)
        {
            if (root.Children[i].Type == FileEntryType.Directory)
            {
                lastDirIndex = i;
            }
        }
        // All directories should appear before all files
        if (lastDirIndex >= 0 && firstFileIndex >= 0)
        {
            Assert.True(lastDirIndex < firstFileIndex,
                $"Last dir at {lastDirIndex}, first file at {firstFileIndex}. Names: {string.Join(", ", names)}");
        }
    }

    [Fact]
    public void Build_GlobFilter_PrunesEmptyBranches()
    {
        var builder = new TreeBuilder(MakeOptions(globPatterns: new[] { "*.cs" }));
        TreeNode root = builder.Build(_root);

        // "empty/" should be pruned (no .cs files inside)
        Assert.DoesNotContain(root.Children, c => c.Name == "empty");
        // "sub/" should remain (has .cs files)
        Assert.Contains(root.Children, c => c.Name == "sub");
        // beta.txt should be gone
        Assert.DoesNotContain(root.Children, c => c.Name == "beta.txt");
    }

    [Fact]
    public void Build_MaxDepth_LimitsRecursion()
    {
        var builder = new TreeBuilder(MakeOptions(maxDepth: 1));
        TreeNode root = builder.Build(_root);

        // sub/ should appear but sub/deep/ should not be expanded
        TreeNode? sub = root.Children.FirstOrDefault(c => c.Name == "sub");
        Assert.NotNull(sub);
        Assert.DoesNotContain(sub!.Children, c => c.Name == "deep");
    }

    [Fact]
    public void Build_NoHidden_SkipsHiddenFiles()
    {
        var builder = new TreeBuilder(MakeOptions(includeHidden: false));
        TreeNode root = builder.Build(_root);

        Assert.DoesNotContain(root.Children, c => c.Name == ".hidden");
    }

    [Fact]
    public void Build_ComputeSizes_RollsUpDirectorySizes()
    {
        var builder = new TreeBuilder(MakeOptions(computeSizes: true));
        TreeNode root = builder.Build(_root);

        // sub/ should have size = gamma (30) + delta (40) = 70
        TreeNode? sub = root.Children.FirstOrDefault(c => c.Name == "sub");
        Assert.NotNull(sub);
        Assert.Equal(70, sub!.SizeBytes);
    }

    [Fact]
    public void Build_SortBySize_LargestFirst()
    {
        var builder = new TreeBuilder(MakeOptions(computeSizes: true, sort: SortMode.Size));
        TreeNode root = builder.Build(_root);

        var files = root.Children.Where(c => c.Type == FileEntryType.File).ToList();
        if (files.Count >= 2)
        {
            Assert.True(files[0].SizeBytes >= files[1].SizeBytes,
                $"Expected descending size: {files[0].SizeBytes} >= {files[1].SizeBytes}");
        }
    }

    [Fact]
    public void Build_FilteredTree_AncestorDirsHaveIsMatchFalse()
    {
        var builder = new TreeBuilder(MakeOptions(globPatterns: new[] { "*.cs" }));
        TreeNode root = builder.Build(_root);

        // sub/ is kept as ancestor but didn't directly match
        TreeNode? sub = root.Children.FirstOrDefault(c => c.Name == "sub");
        Assert.NotNull(sub);
        Assert.False(sub!.IsMatch);

        // gamma.cs matched
        TreeNode? gamma = sub.Children.FirstOrDefault(c => c.Name == "gamma.cs");
        Assert.NotNull(gamma);
        Assert.True(gamma!.IsMatch);
    }

    private void CreateFile(string relativePath, string content)
    {
        string fullPath = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(fullPath, content);
    }

    private static TreeBuilderOptions MakeOptions(
        string[]? globPatterns = null,
        string[]? regexPatterns = null,
        FileEntryType? typeFilter = null,
        long? minSize = null,
        long? maxSize = null,
        DateTimeOffset? newerThan = null,
        DateTimeOffset? olderThan = null,
        int? maxDepth = null,
        bool includeHidden = true,
        bool useGitIgnore = false,
        bool caseInsensitive = false,
        bool computeSizes = false,
        SortMode sort = SortMode.Name)
    {
        return new TreeBuilderOptions(
            GlobPatterns: (IReadOnlyList<string>)(globPatterns ?? Array.Empty<string>()),
            RegexPatterns: (IReadOnlyList<string>)(regexPatterns ?? Array.Empty<string>()),
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
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.TreeX.Tests --filter "FullyQualifiedName~TreeBuilderTests" -v quiet`
Expected: Build error — `TreeBuilder` does not exist

- [ ] **Step 3: Implement TreeBuilder**

Create `src/Winix.TreeX/TreeBuilder.cs`. Key responsibilities:

1. **`Build(string rootPath)`** — creates the root `TreeNode`, calls `BuildRecursive` for children
2. **`BuildRecursive(string dirPath, int depth)`** — enumerates directory entries, creates child `TreeNode`s, recurses for subdirectories
3. **Predicate filtering** — uses `GlobMatcher` (from Winix.FileWalk) and `Regex` for file matching. Directories are never filtered by glob/regex (only files are). Size, date, type filters applied to files.
4. **Sorting** — after building children, sort: directories first (alphabetical), then files (alphabetical). For `SortMode.Size`: directories first by size descending, files by size descending. For `SortMode.Modified`: directories first by date descending, files by date descending.
5. **Pruning** — after filtering, call `PruneEmpty(node)`: recursively remove directories that have no matching descendants. A directory is kept if it has any child with `IsMatch = true` or any child directory that survived pruning.
6. **Size rollup** — when `ComputeSizes` is true, call `RollUpSizes(node)`: for each directory, `SizeBytes = sum of children's SizeBytes`. Bottom-up traversal.
7. **Hidden detection** — same logic as FileWalker: dot-prefix check + Windows `FileAttributes.Hidden`
8. **Gitignore** — accept `Func<string, bool>? isIgnored` in constructor (same pattern as FileWalker)
9. **Executable detection** — on Unix: `File.GetUnixFileMode(path)` has `UnixFileMode.UserExecute`. On Windows: check extension against `.exe`, `.cmd`, `.bat`, `.ps1`, `.com`. Use `RuntimeInformation.IsOSPlatform()` to branch.

The implementation should use `Directory.EnumerateFileSystemEntries()` with try/catch for `UnauthorizedAccessException` and `DirectoryNotFoundException` (skip inaccessible directories).

When `MaxDepth` is set: stop recursing when `depth >= MaxDepth`. Still show the directory node itself, just don't expand its children.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Winix.TreeX.Tests --filter "FullyQualifiedName~TreeBuilderTests" -v quiet`
Expected: All tests pass

- [ ] **Step 5: Commit**

```bash
git add src/Winix.TreeX/TreeBuilder.cs tests/Winix.TreeX.Tests/TreeBuilderTests.cs
git commit -m "feat(treex): TreeBuilder — recursive walk, filter, sort, prune, size rollup"
```

---

## Task 5: TreeRenderer

Walks the TreeNode tree and produces formatted output with tree-line characters, colour, OSC 8 links, and optional size/date columns.

**Files:**
- Create: `src/Winix.TreeX/TreeRenderer.cs`
- Create: `tests/Winix.TreeX.Tests/TreeRendererTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/Winix.TreeX.Tests/TreeRendererTests.cs
using Winix.FileWalk;
using Winix.TreeX;
using Xunit;

namespace Winix.TreeX.Tests;

public class TreeRendererTests
{
    private static TreeNode MakeTree()
    {
        // root/
        //   sub/
        //     gamma.cs
        //   alpha.cs
        //   beta.txt
        var root = MakeDir("root");
        var sub = MakeDir("sub");
        sub.Children.Add(MakeFile("gamma.cs", 30));
        root.Children.Add(sub);
        root.Children.Add(MakeFile("alpha.cs", 10));
        root.Children.Add(MakeFile("beta.txt", 20));
        return root;
    }

    [Fact]
    public void Render_BasicTree_UsesCorrectConnectors()
    {
        TreeNode root = MakeTree();
        string output = RenderToString(root, new TreeRenderOptions(false, false, false, false, false));

        Assert.Contains("├── alpha.cs", output);
        Assert.Contains("└── beta.txt", output);
        Assert.Contains("├── sub", output);
        Assert.Contains("│   └── gamma.cs", output);
    }

    [Fact]
    public void Render_LastChild_UsesElbow()
    {
        var root = MakeDir("root");
        root.Children.Add(MakeFile("only.txt", 10));

        string output = RenderToString(root, new TreeRenderOptions(false, false, false, false, false));

        Assert.Contains("└── only.txt", output);
        Assert.DoesNotContain("├──", output);
    }

    [Fact]
    public void Render_ReturnsCorrectStats()
    {
        TreeNode root = MakeTree();
        var writer = new StringWriter();
        var renderer = new TreeRenderer(new TreeRenderOptions(false, false, false, false, false));
        TreeStats stats = renderer.Render(root, writer);

        Assert.Equal(1, stats.DirectoryCount);
        Assert.Equal(3, stats.FileCount);
    }

    [Fact]
    public void Render_WithColor_DirectoriesAreBlue()
    {
        TreeNode root = MakeTree();
        string output = RenderToString(root, new TreeRenderOptions(true, false, false, false, false));

        // Directory "sub" should have blue ANSI code
        Assert.Contains("\x1b[34m", output);
        Assert.Contains("\x1b[0m", output);
    }

    [Fact]
    public void Render_NoColor_NoAnsiCodes()
    {
        TreeNode root = MakeTree();
        string output = RenderToString(root, new TreeRenderOptions(false, false, false, false, false));

        Assert.False(output.Contains('\x1b'));
    }

    [Fact]
    public void Render_WithColor_ConnectorsAreDim()
    {
        TreeNode root = MakeTree();
        string output = RenderToString(root, new TreeRenderOptions(true, false, false, false, false));

        // Dim code should appear (for tree connectors)
        Assert.Contains("\x1b[2m", output);
    }

    [Fact]
    public void Render_WithSize_ShowsSizeColumn()
    {
        TreeNode root = MakeTree();
        root.Children[0].SizeBytes = 1024; // sub/ = 1024
        string output = RenderToString(root, new TreeRenderOptions(false, false, true, false, false));

        Assert.Contains("1.0K", output);
    }

    [Fact]
    public void Render_WithLinks_EmitsOsc8()
    {
        TreeNode root = MakeTree();
        string output = RenderToString(root, new TreeRenderOptions(false, true, false, false, false));

        // OSC 8 link format
        Assert.Contains("\x1b]8;;file://", output);
        Assert.Contains("\x1b]8;;\x1b\\", output);
    }

    [Fact]
    public void Render_DirsOnly_SuppressesFiles()
    {
        TreeNode root = MakeTree();
        string output = RenderToString(root, new TreeRenderOptions(false, false, false, false, true));

        Assert.Contains("sub", output);
        Assert.DoesNotContain("alpha.cs", output);
        Assert.DoesNotContain("gamma.cs", output);
    }

    [Fact]
    public void Render_NestedIndentation_UsesBarForNonLastParent()
    {
        // root/
        //   dir1/
        //     file1.cs
        //   dir2/
        //     file2.cs
        var root = MakeDir("root");
        var dir1 = MakeDir("dir1");
        dir1.Children.Add(MakeFile("file1.cs", 10));
        var dir2 = MakeDir("dir2");
        dir2.Children.Add(MakeFile("file2.cs", 10));
        root.Children.Add(dir1);
        root.Children.Add(dir2);

        string output = RenderToString(root, new TreeRenderOptions(false, false, false, false, false));

        // dir1's child should be indented with │ (because dir1 is not the last sibling)
        Assert.Contains("│   └── file1.cs", output);
        // dir2's child should be indented with spaces (because dir2 IS the last sibling)
        Assert.Contains("    └── file2.cs", output);
    }

    private static string RenderToString(TreeNode root, TreeRenderOptions options)
    {
        var writer = new StringWriter();
        var renderer = new TreeRenderer(options);
        renderer.Render(root, writer);
        return writer.ToString();
    }

    private static TreeNode MakeDir(string name)
    {
        return new TreeNode
        {
            Name = name,
            FullPath = Path.Combine(Path.GetTempPath(), name),
            Type = FileEntryType.Directory,
            SizeBytes = -1,
            Modified = DateTimeOffset.Now
        };
    }

    private static TreeNode MakeFile(string name, long size)
    {
        return new TreeNode
        {
            Name = name,
            FullPath = Path.Combine(Path.GetTempPath(), name),
            Type = FileEntryType.File,
            SizeBytes = size,
            Modified = DateTimeOffset.Now
        };
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Winix.TreeX.Tests --filter "FullyQualifiedName~TreeRendererTests" -v quiet`
Expected: Build error — `TreeRenderer` does not exist

- [ ] **Step 3: Implement TreeRenderer**

Create `src/Winix.TreeX/TreeRenderer.cs`. Key responsibilities:

1. **`Render(TreeNode root, TextWriter writer)`** — writes the root name, then calls `RenderChildren` for each child. Returns `TreeStats`.
2. **`RenderChildren(List<TreeNode> children, TextWriter writer, string prefix)`** — iterates children. For each child:
   - Determine connector: `├── ` for non-last, `└── ` for last child
   - Determine child prefix: `│   ` for non-last parent, `    ` for last parent
   - Write the line: `{prefix}{connector}{formatted name}{optional size}{optional date}`
   - If directory, recurse with updated prefix
3. **Colour** — when `UseColor`:
   - Tree connectors (`├──`, `└──`, `│`) wrapped in `AnsiColor.Dim(true)` + `AnsiColor.Reset(true)`
   - Directory names in `AnsiColor.Blue(true)`
   - Symlink names in `AnsiColor.Cyan(true)`
   - Executable names in `AnsiColor.Green(true)`
   - Size/date in `AnsiColor.Dim(true)`
4. **OSC 8 links** — when `UseLinks`: wrap the display name in `\x1b]8;;file:///{absolutePath}\x1b\\{name}\x1b]8;;\x1b\\`. Paths must be absolute and URL-encoded for special characters.
5. **Size column** — when `ShowSize`: compute max size width across all entries, right-pad name to align, append right-aligned size using `HumanSize.FormatPadded`.
6. **Date column** — when `ShowDate`: append `yyyy-MM-dd HH:mm` in local time after size (or after name if no size).
7. **DirsOnly** — skip file nodes entirely. Only render directories and their children.
8. **Stats counting** — count directories and files during rendering. Track total size.

Tree connector characters (Unicode box drawing):
- `├── ` (T-junction + horizontal + horizontal + space)
- `└── ` (elbow + horizontal + horizontal + space)
- `│   ` (vertical + three spaces)
- `    ` (four spaces, for last-child continuation)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Winix.TreeX.Tests --filter "FullyQualifiedName~TreeRendererTests" -v quiet`
Expected: All tests pass

- [ ] **Step 5: Commit**

```bash
git add src/Winix.TreeX/TreeRenderer.cs tests/Winix.TreeX.Tests/TreeRendererTests.cs
git commit -m "feat(treex): TreeRenderer — tree lines, colour, OSC 8 links, size/date columns"
```

---

## Task 6: Formatting (NDJSON, JSON summary)

**Files:**
- Create: `src/Winix.TreeX/Formatting.cs`
- Create: `tests/Winix.TreeX.Tests/FormattingTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/Winix.TreeX.Tests/FormattingTests.cs
using Winix.FileWalk;
using Winix.TreeX;
using Xunit;

namespace Winix.TreeX.Tests;

public class FormattingTests
{
    [Fact]
    public void FormatNdjsonLine_ContainsStandardFields()
    {
        var node = new TreeNode
        {
            Name = "test.cs",
            FullPath = "/tmp/test.cs",
            Type = FileEntryType.File,
            SizeBytes = 1234,
            Modified = new DateTimeOffset(2026, 3, 31, 14, 0, 0, TimeSpan.FromHours(13))
        };

        string json = Formatting.FormatNdjsonLine(node, 1, "treex", "1.0.0");

        Assert.Contains("\"tool\":\"treex\"", json);
        Assert.Contains("\"version\":\"1.0.0\"", json);
        Assert.Contains("\"exit_code\":0", json);
        Assert.Contains("\"exit_reason\":\"success\"", json);
        Assert.Contains("\"name\":\"test.cs\"", json);
        Assert.Contains("\"type\":\"file\"", json);
        Assert.Contains("\"size_bytes\":1234", json);
        Assert.Contains("\"depth\":1", json);
    }

    [Fact]
    public void FormatJsonSummary_ContainsStandardFields()
    {
        var stats = new TreeStats(3, 10, 48000);
        string json = Formatting.FormatJsonSummary(stats, 0, "success", "treex", "1.0.0");

        Assert.Contains("\"tool\":\"treex\"", json);
        Assert.Contains("\"directories\":3", json);
        Assert.Contains("\"files\":10", json);
        Assert.Contains("\"total_size_bytes\":48000", json);
    }

    [Fact]
    public void FormatJsonSummary_OmitsSizeWhenNegative()
    {
        var stats = new TreeStats(3, 10, -1);
        string json = Formatting.FormatJsonSummary(stats, 0, "success", "treex", "1.0.0");

        Assert.DoesNotContain("total_size_bytes", json);
    }

    [Fact]
    public void FormatJsonError_ContainsErrorFields()
    {
        string json = Formatting.FormatJsonError(125, "usage_error", "treex", "1.0.0");

        Assert.Contains("\"exit_code\":125", json);
        Assert.Contains("\"exit_reason\":\"usage_error\"", json);
    }

    [Fact]
    public void FormatSummaryLine_BasicFormat()
    {
        var stats = new TreeStats(3, 10, -1);
        string line = Formatting.FormatSummaryLine(stats);

        Assert.Equal("3 directories, 10 files", line);
    }

    [Fact]
    public void FormatSummaryLine_WithSize()
    {
        var stats = new TreeStats(3, 10, 48000);
        string line = Formatting.FormatSummaryLine(stats);

        Assert.Contains("3 directories, 10 files", line);
        Assert.Contains("46.9K", line);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

- [ ] **Step 3: Implement Formatting**

Create `src/Winix.TreeX/Formatting.cs` with methods:
- `FormatNdjsonLine(TreeNode node, int depth, string toolName, string version)` — JSON line with standard envelope + node fields
- `FormatJsonSummary(TreeStats stats, int exitCode, string exitReason, string toolName, string version)` — summary object. Include `total_size_bytes` only when >= 0.
- `FormatJsonError(int exitCode, string exitReason, string toolName, string version)` — error envelope
- `FormatSummaryLine(TreeStats stats)` — `"N directories, M files"` with optional size in parentheses

Follow the same hand-crafted JSON pattern as Winix.Files.Formatting (StringBuilder, EscapeJson helper).

- [ ] **Step 4: Run tests to verify they pass**

- [ ] **Step 5: Commit**

```bash
git add src/Winix.TreeX/Formatting.cs tests/Winix.TreeX.Tests/FormattingTests.cs
git commit -m "feat(treex): NDJSON, JSON summary, and summary line formatting"
```

---

## Task 7: Console App (Program.cs)

**Files:**
- Replace: `src/treex/Program.cs`

- [ ] **Step 1: Implement Program.cs**

The thin console app. Pattern follows `src/files/Program.cs` exactly. Key responsibilities:

1. `ConsoleEnv.EnableAnsiIfNeeded()` at startup
2. Register all CLI flags with `CommandLineParser`
3. Register `--describe` metadata (Platform, I/O, Examples, ComposesWith, JsonField)
4. Parse args, handle `IsHandled`/`HasErrors`
5. Validate mutually exclusive flags (`--ignore-case`/`--case-sensitive`)
6. Convert `--ext` values to glob patterns
7. Parse `--min-size`, `--max-size`, `--newer`, `--older`
8. Resolve case sensitivity (platform default)
9. Resolve type filter from `--type`
10. Resolve sort mode from `--sort`
11. Build `TreeBuilderOptions`
12. Validate root paths exist
13. Create `GitIgnoreFilter` if `--gitignore`
14. Resolve colour and link support
15. For each root: build tree, render tree (or emit NDJSON), collect stats
16. Write summary line to stderr
17. Write JSON summary to stderr if `--json`
18. Clean up GitIgnoreFilter

**`--describe` metadata:**
```csharp
.Platform("cross-platform",
    replaces: new[] { "tree" },
    valueOnWindows: "Windows tree is DOS-era — no colour, filtering, or sizes",
    valueOnUnix: "Adds clickable hyperlinks, size rollups, gitignore, JSON output")
.StdinDescription("Not used")
.StdoutDescription("Tree-formatted directory listing. NDJSON with --ndjson.")
.StderrDescription("Summary line, errors, and --json output.")
.Example("treex", "Show current directory tree")
.Example("treex src --ext cs", "Show only C# files with ancestor directories")
.Example("treex --size --gitignore --no-hidden", "Clean tree with sizes")
.Example("treex --size --sort size", "Find largest files")
.Example("treex -d 2", "Limit to 2 levels deep")
.Example("treex src tests", "Show multiple roots")
.ComposesWith("files", "files for piping, treex for visual display", "Use files for machine processing, treex for human viewing of the same directory")
```

Exit codes: 0 (success), 1 (runtime error), 125 (usage error)

Version: `typeof(TreeBuilder).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0"`

- [ ] **Step 2: Verify build**

Run: `dotnet build Winix.sln`

- [ ] **Step 3: Smoke test**

```bash
dotnet run --project src/treex -- src --ext cs --no-hidden --gitignore -d 2
dotnet run --project src/treex -- --describe
dotnet run --project src/treex -- --help
dotnet run --project src/treex -- src --size --no-hidden --gitignore
```

- [ ] **Step 4: Commit**

```bash
git add src/treex/Program.cs
git commit -m "feat(treex): console app with arg parsing, pipeline wiring, and --describe metadata"
```

---

## Task 8: README, AI Guide, and Integration

**Files:**
- Replace: `src/treex/README.md`
- Create: `docs/ai/treex.md`
- Modify: `llms.txt`
- Modify: `CLAUDE.md`
- Modify: `README.md`

- [ ] **Step 1: Write treex README**

Follow the pattern of `src/files/README.md`. Include: description, install (Scoop/winget/.NET tool/download), usage examples, full options table, exit codes, colour section, differences from tree.

Key examples:
```bash
treex                                    # Current directory
treex src --ext cs                       # C# files only (pruned tree)
treex --size --gitignore --no-hidden     # Clean tree with sizes
treex --size --sort size                 # Disk usage investigation
treex -d 2                               # Two levels deep
treex src tests                          # Multiple roots
treex --ndjson                           # Machine-parseable output
treex --describe                         # AI agent metadata
```

- [ ] **Step 2: Write AI guide**

Create `docs/ai/treex.md` following the template (What This Tool Does, Platform Story, When to Use This, Common Patterns, Composing with Other Tools, Gotchas, Getting Structured Data).

Key content: treex is for VISUAL directory inspection, not piping. Use `files` for piping. treex adds value via colour, hyperlinks, size rollups, and pruned filtering.

- [ ] **Step 3: Update llms.txt**

Add treex to the Tools section:
```markdown
- [treex](docs/ai/treex.md): Enhanced directory tree with colour, filtering, sizes, and clickable links. Replaces `tree`.
```

- [ ] **Step 4: Update CLAUDE.md**

Add to project layout:
```
src/Winix.TreeX/           — class library (tree building, rendering)
src/treex/                 — console app entry point
tests/Winix.TreeX.Tests/   — xUnit tests
```

Add `Winix.TreeX` to NuGet package IDs. Add `treex.json` to scoop manifests list.

- [ ] **Step 5: Update README.md**

Add treex to the shipped tools table.

- [ ] **Step 6: Commit**

```bash
git add src/treex/README.md docs/ai/treex.md llms.txt CLAUDE.md README.md
git commit -m "docs: README, AI guide, and integration for treex"
```

---

## Task 9: Release Pipeline and Scoop

**Files:**
- Modify: `.github/workflows/release.yml`
- Create: `bucket/treex.json`
- Modify: `bucket/winix.json`

- [ ] **Step 1: Add treex to release.yml**

Add `treex` to all pipeline steps where the other tools appear (pack-nuget, publish-aot, zip steps, combined zip, scoop update, winget generation). Follow the exact pattern of the `files` entries.

- [ ] **Step 2: Create scoop manifest**

Create `bucket/treex.json` following the pattern of `bucket/files.json`.

- [ ] **Step 3: Update combined scoop manifest**

Add `"treex.exe"` to `bucket/winix.json` bin array.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/release.yml bucket/treex.json bucket/winix.json
git commit -m "chore: add treex to release pipeline, scoop manifests, and winget"
```

---

## Task 10: Final Verification

- [ ] **Step 1: Full build and test**

Run: `dotnet build Winix.sln`
Expected: 0 warnings, 0 errors

Run: `dotnet test Winix.sln`
Expected: All tests pass

- [ ] **Step 2: Smoke tests**

```bash
# Basic tree
dotnet run --project src/treex -- src --no-hidden --gitignore

# With sizes
dotnet run --project src/treex -- src --size --no-hidden --gitignore

# Filtered
dotnet run --project src/treex -- src --ext cs --no-hidden --gitignore

# NDJSON
dotnet run --project src/treex -- src --ext cs --ndjson --no-hidden --gitignore

# Describe
dotnet run --project src/treex -- --describe | python -m json.tool

# Multiple roots
dotnet run --project src/treex -- src tests -d 1 --no-hidden --gitignore
```

- [ ] **Step 3: Trial NuGet pack**

```bash
dotnet pack src/treex/treex.csproj -c Release -o /tmp/treex-pack-test
unzip -l /tmp/treex-pack-test/Winix.TreeX.*.nupkg | grep -i winix.png
```

- [ ] **Step 4: Push**

```bash
git push
```
