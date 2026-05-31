# demux Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `demux` — a streaming stdin→stdout pipe filter that routes each input line to one of N sinks (files or commands) by regex, passing unmatched records through.

**Architecture:** Class library `Winix.Demux` (data model, sinks, router, summary, arg parsing, `Cli.Run` seam) + thin console app `src/demux`. Self-contained route flags (`--to PATTERN FILE` / `--exec PATTERN CMD`) parsed via a custom pre-pass that peels route operands out of argv before handing the residual to ShellKit's `CommandLineParser`. Reuses ShellKit `SafeRegex`, `ConsoleEnv`, `JsonHelper`, `ExitCode`.

**Tech Stack:** C# / .NET 10, NativeAOT, xUnit + Xunit.SkippableFact, `Yort.ShellKit`.

**Spec:** `docs/plans/2026-05-31-demux-design.md` + ADR `docs/plans/2026-05-31-demux-adr.md`.

---

## File structure

**`src/Winix.Demux/` (class library):**
- `TargetKind.cs` — enum `{ File, Exec }`.
- `RouteSpec.cs` — one route: compiled `Regex` predicate + `TargetKind` + target string + original pattern text.
- `DemuxOptions.cs` — parsed run config: routes, optional default target, field, delimiter, all/append/exit-on-child-error, json, color.
- `ISink.cs` — sink contract (`Write`, `Close`, delivered/undelivered counts, dead state, child exit code).
- `StdoutSink.cs` — passthrough sink wrapping a `TextWriter`.
- `FileSink.cs` — file target (truncate/append).
- `CommandSink.cs` — shell-spawned command target; broken-pipe-safe; captures child exit code.
- `Router.cs` — core routing loop (predicate eval, first-match/--all, field, unmatched→default/stdout).
- `RoutingSummary.cs` — per-sink counts, dead routes, exit-code computation (0/1/2 precedence), human + JSON rendering.
- `ArgParser.cs` — custom route scan + ShellKit residual parse + regex compile + validation; `BuildParser` for help/describe.
- `Cli.cs` — `Run(args, stdin, stdout, stderr)` orchestrator.

**`src/demux/`:** `Program.cs` (thin shim), `demux.csproj`, `README.md`, `man/man1/demux.1`.

**`tests/Winix.Demux.Tests/`:** `RouterTests.cs`, `FileSinkTests.cs`, `RoutingSummaryTests.cs`, `ArgParserTests.cs`, `CliTests.cs`, `IntegrationTests_CommandSink.cs`, plus a shared `FakeSink` test double.

**Docs/wiring:** `docs/ai/demux.md`, `llms.txt`, `bucket/demux.json`, `.github/workflows/release.yml`, `.github/workflows/post-publish.yml`, `CLAUDE.md`.

---

## Task 1: Project scaffolding

**Files:**
- Create: `src/Winix.Demux/Winix.Demux.csproj`
- Create: `src/demux/demux.csproj`
- Create: `tests/Winix.Demux.Tests/Winix.Demux.Tests.csproj`
- Modify: `Winix.sln`

- [ ] **Step 1: Create the class-library csproj**

Mirror `src/Winix.Trash/Winix.Trash.csproj` exactly, changing the assembly/root-namespace to `Winix.Demux`. It must reference `Yort.ShellKit`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Yort.ShellKit\Yort.ShellKit.csproj" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="Winix.Demux.Tests" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the console-app csproj**

Mirror `src/trash/trash.csproj`, changing `PackageId` to `Winix.HCat`→`Winix.Demux`, `<AssemblyName>demux</AssemblyName>`, `<Description>` to *"Route each line of a stream to files or commands by regex — the partition verb for pipes."*, `<PackageTags>` to the shared baseline `cli;command-line;cross-platform;windows;macos;linux;aot;dotnet-tool;winix` plus `pipe;filter;router;partition;stream;awk`. Reference `..\Winix.Demux\Winix.Demux.csproj`. Include the man page (added in Task 11):
```xml
<Content Include="man\man1\demux.1" CopyToPublishDirectory="PreserveNewest" Link="share\man\man1\demux.1" />
```

- [ ] **Step 3: Create the test csproj**

Mirror `tests/Winix.Trash.Tests/Winix.Trash.Tests.csproj` (xUnit + Xunit.SkippableFact + `InvariantGlobalization=true` on the test project per the resource-key memory). Reference `..\..\src\Winix.Demux\Winix.Demux.csproj`.

- [ ] **Step 4: Add all three projects to the solution**

Run:
```bash
dotnet sln Winix.sln add src/Winix.Demux/Winix.Demux.csproj src/demux/demux.csproj tests/Winix.Demux.Tests/Winix.Demux.Tests.csproj
```

- [ ] **Step 5: Verify it builds**

Run: `dotnet build Winix.sln`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add src/Winix.Demux src/demux tests/Winix.Demux.Tests Winix.sln
git commit -m "chore(demux): project scaffolding (lib + console + tests)"
```

---

## Task 2: Data model — TargetKind, RouteSpec, DemuxOptions

**Files:**
- Create: `src/Winix.Demux/TargetKind.cs`
- Create: `src/Winix.Demux/RouteSpec.cs`
- Create: `src/Winix.Demux/DemuxOptions.cs`

- [ ] **Step 1: Create `TargetKind.cs`**

```csharp
namespace Winix.Demux;

/// <summary>The kind of sink a route delivers to.</summary>
public enum TargetKind
{
    /// <summary>Write matching lines to a file.</summary>
    File,
    /// <summary>Feed matching lines to a shell-spawned command's stdin.</summary>
    Exec,
}
```

- [ ] **Step 2: Create `RouteSpec.cs`**

```csharp
using System.Text.RegularExpressions;

namespace Winix.Demux;

/// <summary>
/// One route: a compiled regex predicate bound to a typed target. The predicate is null for the
/// default route (it matches everything unmatched by the explicit routes).
/// </summary>
public sealed class RouteSpec
{
    /// <summary>Creates a predicate route.</summary>
    public RouteSpec(Regex predicate, string patternText, TargetKind kind, string target)
    {
        Predicate = predicate;
        PatternText = patternText;
        Kind = kind;
        Target = target;
    }

    /// <summary>Creates the default (predicate-less) route.</summary>
    private RouteSpec(TargetKind kind, string target)
    {
        Predicate = null;
        PatternText = "(default)";
        Kind = kind;
        Target = target;
    }

    /// <summary>The compiled regex, or null for the default route.</summary>
    public Regex? Predicate { get; }

    /// <summary>The original pattern text (for labels/summary). "(default)" for the default route.</summary>
    public string PatternText { get; }

    /// <summary>File or Exec.</summary>
    public TargetKind Kind { get; }

    /// <summary>The file path (File) or command string (Exec).</summary>
    public string Target { get; }

    /// <summary>True if this is the default route.</summary>
    public bool IsDefault => Predicate is null;

    /// <summary>Factory for the default route.</summary>
    public static RouteSpec Default(TargetKind kind, string target) => new(kind, target);
}
```

- [ ] **Step 3: Create `DemuxOptions.cs`**

```csharp
using System.Collections.Generic;

namespace Winix.Demux;

/// <summary>Fully-parsed, validated run configuration for a demux invocation.</summary>
public sealed class DemuxOptions
{
    public DemuxOptions(
        IReadOnlyList<RouteSpec> routes,
        RouteSpec? defaultRoute,
        int? field,
        string delimiter,
        bool all,
        bool append,
        bool exitOnChildError,
        bool json,
        bool useColor)
    {
        Routes = routes;
        DefaultRoute = defaultRoute;
        Field = field;
        Delimiter = delimiter;
        All = all;
        Append = append;
        ExitOnChildError = exitOnChildError;
        Json = json;
        UseColor = useColor;
    }

    /// <summary>Explicit routes, in declaration order (at least one).</summary>
    public IReadOnlyList<RouteSpec> Routes { get; }

    /// <summary>The default route, or null (unmatched → stdout).</summary>
    public RouteSpec? DefaultRoute { get; }

    /// <summary>1-based field index to test, or null (test the whole line).</summary>
    public int? Field { get; }

    /// <summary>Field delimiter; the sentinel "" means "runs of whitespace" (awk default).</summary>
    public string Delimiter { get; }

    /// <summary>Broadcast to every matching route instead of first-match.</summary>
    public bool All { get; }

    /// <summary>File targets append instead of truncate.</summary>
    public bool Append { get; }

    /// <summary>A watched child's non-zero exit makes demux exit 2.</summary>
    public bool ExitOnChildError { get; }

    /// <summary>Emit the summary as JSON (to stderr).</summary>
    public bool Json { get; }

    /// <summary>Use ANSI colour in the human summary.</summary>
    public bool UseColor { get; }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Winix.Demux/Winix.Demux.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Demux/TargetKind.cs src/Winix.Demux/RouteSpec.cs src/Winix.Demux/DemuxOptions.cs
git commit -m "feat(demux): data model — TargetKind, RouteSpec, DemuxOptions"
```

---

## Task 3: ISink + StdoutSink + FakeSink test double

**Files:**
- Create: `src/Winix.Demux/ISink.cs`
- Create: `src/Winix.Demux/StdoutSink.cs`
- Create: `tests/Winix.Demux.Tests/FakeSink.cs`
- Test: `tests/Winix.Demux.Tests/StdoutSinkTests.cs`

- [ ] **Step 1: Create `ISink.cs`**

```csharp
namespace Winix.Demux;

/// <summary>
/// A delivery target for routed lines. Implementations must be broken-pipe-safe: a write that
/// fails (e.g. a child closed its stdin) marks the sink dead and counts the record as undelivered
/// rather than throwing — one failing sink must never starve its siblings or crash the router.
/// </summary>
public interface ISink
{
    /// <summary>Human label for the summary (e.g. the pattern or "(default)"/"stdout").</summary>
    string Label { get; }

    /// <summary>Writes one line (a trailing newline is added). Never throws on broken pipe.</summary>
    void Write(string line);

    /// <summary>Flushes and closes; for command sinks, closes stdin, waits, captures the exit code.</summary>
    void Close();

    /// <summary>Lines successfully written.</summary>
    long DeliveredCount { get; }

    /// <summary>Lines that could not be delivered because the sink died mid-run.</summary>
    long UndeliveredCount { get; }

    /// <summary>True once a write failed and the sink stopped accepting records.</summary>
    bool IsDead { get; }

    /// <summary>The child's exit code (Exec sinks only, after Close); null otherwise.</summary>
    int? ChildExitCode { get; }
}
```

- [ ] **Step 2: Create `StdoutSink.cs`**

```csharp
using System.IO;

namespace Winix.Demux;

/// <summary>
/// Passthrough sink for unmatched records — wraps demux's own stdout writer. A broken pipe here
/// (downstream consumer closed) marks the sink dead and counts undelivered, like any other sink.
/// </summary>
public sealed class StdoutSink : ISink
{
    private readonly TextWriter _writer;
    private bool _dead;
    private long _delivered;
    private long _undelivered;

    public StdoutSink(TextWriter writer, string label = "stdout")
    {
        _writer = writer;
        Label = label;
    }

    public string Label { get; }
    public long DeliveredCount => _delivered;
    public long UndeliveredCount => _undelivered;
    public bool IsDead => _dead;
    public int? ChildExitCode => null;

    public void Write(string line)
    {
        if (_dead) { _undelivered++; return; }
        try
        {
            // Write '\n' explicitly (not WriteLine) so we don't rewrite LF input to CRLF on Windows
            // or append a terminator the original line didn't have. A router preserves line bytes.
            _writer.Write(line);
            _writer.Write('\n');
            _delivered++;
        }
        catch (IOException)
        {
            _dead = true;
            _undelivered++;
        }
    }

    public void Close()
    {
        try { _writer.Flush(); } catch (IOException) { /* downstream gone; nothing to do */ }
    }
}
```

- [ ] **Step 3: Create the `FakeSink` test double**

```csharp
using System.Collections.Generic;
using Winix.Demux;

namespace Winix.Demux.Tests;

/// <summary>In-memory sink for Router tests. Optionally simulates death after N writes.</summary>
internal sealed class FakeSink : ISink
{
    private readonly int _dieAfter; // -1 = never die
    public FakeSink(string label, int dieAfter = -1) { Label = label; _dieAfter = dieAfter; }

    public List<string> Lines { get; } = new();
    public string Label { get; }
    public long DeliveredCount { get; private set; }
    public long UndeliveredCount { get; private set; }
    public bool IsDead { get; private set; }
    public int? ChildExitCode { get; set; }
    public bool Closed { get; private set; }

    public void Write(string line)
    {
        if (IsDead) { UndeliveredCount++; return; }
        if (_dieAfter >= 0 && Lines.Count >= _dieAfter) { IsDead = true; UndeliveredCount++; return; }
        Lines.Add(line);
        DeliveredCount++;
    }

    public void Close() => Closed = true;
}
```

- [ ] **Step 4: Write `StdoutSinkTests.cs`**

```csharp
#nullable enable
using System.IO;
using Winix.Demux;
using Xunit;

namespace Winix.Demux.Tests;

public class StdoutSinkTests
{
    [Fact]
    public void Write_AppendsLinesAndCountsDelivered()
    {
        var sw = new StringWriter();
        var sink = new StdoutSink(sw);

        sink.Write("alpha");
        sink.Write("beta");

        Assert.Equal("alpha\nbeta\n", sw.ToString().Replace("\r\n", "\n"));
        Assert.Equal(2, sink.DeliveredCount);
        Assert.Equal(0, sink.UndeliveredCount);
        Assert.False(sink.IsDead);
    }

    [Fact]
    public void Write_OnBrokenPipe_MarksDeadAndCountsUndelivered()
    {
        var sink = new StdoutSink(new ThrowingWriter());

        sink.Write("x");
        sink.Write("y");

        Assert.True(sink.IsDead);
        Assert.Equal(0, sink.DeliveredCount);
        Assert.Equal(2, sink.UndeliveredCount);
    }

    private sealed class ThrowingWriter : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void WriteLine(string? value) => throw new IOException("broken pipe");
    }
}
```

- [ ] **Step 5: Run tests — expect pass**

Run: `dotnet test tests/Winix.Demux.Tests --filter "FullyQualifiedName~StdoutSinkTests"`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add src/Winix.Demux/ISink.cs src/Winix.Demux/StdoutSink.cs tests/Winix.Demux.Tests/FakeSink.cs tests/Winix.Demux.Tests/StdoutSinkTests.cs
git commit -m "feat(demux): ISink contract + StdoutSink passthrough (broken-pipe-safe)"
```

---

## Task 4: FileSink

**Files:**
- Create: `src/Winix.Demux/FileSink.cs`
- Test: `tests/Winix.Demux.Tests/FileSinkTests.cs`

- [ ] **Step 1: Write `FileSinkTests.cs`**

```csharp
#nullable enable
using System.IO;
using Winix.Demux;
using Xunit;

namespace Winix.Demux.Tests;

public class FileSinkTests
{
    [Fact]
    public void Write_Truncate_OverwritesAndWritesLines()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(path, "pre-existing\n");
        try
        {
            var sink = new FileSink(path, "p", append: false);
            sink.Write("one");
            sink.Write("two");
            sink.Close();

            Assert.Equal("one\ntwo\n", File.ReadAllText(path).Replace("\r\n", "\n"));
            Assert.Equal(2, sink.DeliveredCount);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Write_Append_PreservesExistingContent()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(path, "old\n");
        try
        {
            var sink = new FileSink(path, "p", append: true);
            sink.Write("new");
            sink.Close();

            Assert.Equal("old\nnew\n", File.ReadAllText(path).Replace("\r\n", "\n"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Constructor_UnopenablePath_Throws()
    {
        string bad = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "nope.log"); // missing dir
        Assert.ThrowsAny<IOException>(() => new FileSink(bad, "p", append: false));
    }
}
```

- [ ] **Step 2: Run tests — expect fail (no FileSink)**

Run: `dotnet test tests/Winix.Demux.Tests --filter "FullyQualifiedName~FileSinkTests"`
Expected: FAIL (compile error — FileSink not defined).

- [ ] **Step 3: Implement `FileSink.cs`**

```csharp
using System.IO;

namespace Winix.Demux;

/// <summary>File target. Opens once at construction (truncate or append); an unopenable path throws
/// at construction so the caller can map it to a setup-failure exit (126).</summary>
public sealed class FileSink : ISink
{
    private readonly StreamWriter _writer;
    private bool _dead;
    private long _delivered;
    private long _undelivered;

    public FileSink(string path, string label, bool append)
    {
        // Throws (IOException/UnauthorizedAccess/DirectoryNotFound) if the path can't be opened —
        // caller maps to exit 126.
        _writer = new StreamWriter(path, append) { AutoFlush = false };
        Label = label;
    }

    public string Label { get; }
    public long DeliveredCount => _delivered;
    public long UndeliveredCount => _undelivered;
    public bool IsDead => _dead;
    public int? ChildExitCode => null;

    public void Write(string line)
    {
        if (_dead) { _undelivered++; return; }
        // Write '\n' explicitly (not WriteLine) to preserve line bytes — no LF→CRLF rewrite on Windows.
        try { _writer.Write(line); _writer.Write('\n'); _delivered++; }
        catch (IOException) { _dead = true; _undelivered++; }
    }

    public void Close()
    {
        try { _writer.Flush(); } catch (IOException) { /* disk gone; counts already reflect it */ }
        _writer.Dispose();
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test tests/Winix.Demux.Tests --filter "FullyQualifiedName~FileSinkTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Demux/FileSink.cs tests/Winix.Demux.Tests/FileSinkTests.cs
git commit -m "feat(demux): FileSink (truncate/append, open-failure throws for exit 126)"
```

---

## Task 5: Router

**Files:**
- Create: `src/Winix.Demux/Router.cs`
- Test: `tests/Winix.Demux.Tests/RouterTests.cs`

The Router is the pure core. It receives the input reader, the options, a paired list of
`(RouteSpec, ISink)`, the default sink (or null), and the stdout passthrough sink. It owns no I/O
construction — sinks are injected — so it is fully testable with `FakeSink`.

- [ ] **Step 1: Write `RouterTests.cs`**

```csharp
#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Winix.Demux;
using Xunit;

namespace Winix.Demux.Tests;

public class RouterTests
{
    private static RouteSpec Route(string pat, TargetKind kind = TargetKind.File)
        => new(new Regex(pat), pat, kind, "target");

    private static DemuxOptions Opts(bool all = false, int? field = null, string delim = "")
        => new(new List<RouteSpec>(), null, field, delim, all, false, false, false, false);

    [Fact]
    public void FirstMatch_RoutesToFirstMatchingSinkOnly()
    {
        var s1 = new FakeSink("ERROR");
        var s2 = new FakeSink("R");           // also matches "ERROR" line, but first-match wins
        var stdout = new FakeSink("stdout");
        var routes = new List<(RouteSpec, ISink)> { (Route("ERROR"), s1), (Route("R"), s2) };

        new Router().Run(new StringReader("ERROR here\nplain\n"), Opts(), routes, null, stdout);

        Assert.Equal(new[] { "ERROR here" }, s1.Lines.ToArray());
        Assert.Empty(s2.Lines);
        Assert.Equal(new[] { "plain" }, stdout.Lines.ToArray()); // unmatched → stdout
    }

    [Fact]
    public void All_BroadcastsToEveryMatchingSink()
    {
        var s1 = new FakeSink("ERROR");
        var s2 = new FakeSink("R");
        var stdout = new FakeSink("stdout");
        var routes = new List<(RouteSpec, ISink)> { (Route("ERROR"), s1), (Route("R"), s2) };

        new Router().Run(new StringReader("ERROR here\n"), Opts(all: true), routes, null, stdout);

        Assert.Equal(new[] { "ERROR here" }, s1.Lines.ToArray());
        Assert.Equal(new[] { "ERROR here" }, s2.Lines.ToArray());
        Assert.Empty(stdout.Lines);
    }

    [Fact]
    public void Unmatched_GoesToDefaultSinkWhenPresent()
    {
        var s1 = new FakeSink("ERROR");
        var def = new FakeSink("(default)");
        var stdout = new FakeSink("stdout");
        var routes = new List<(RouteSpec, ISink)> { (Route("ERROR"), s1) };

        new Router().Run(new StringReader("plain\n"), Opts(), routes, def, stdout);

        Assert.Equal(new[] { "plain" }, def.Lines.ToArray());
        Assert.Empty(stdout.Lines);
    }

    [Fact]
    public void Field_TestsChosenColumnOneBased()
    {
        var s1 = new FakeSink("5xx");
        var stdout = new FakeSink("stdout");
        var routes = new List<(RouteSpec, ISink)> { (Route("^5"), s1) };

        // delimiter "" = whitespace; field 2 of "GET 503" is "503"
        new Router().Run(new StringReader("GET 503\nGET 200\n"), Opts(field: 2), routes, null, stdout);

        Assert.Equal(new[] { "GET 503" }, s1.Lines.ToArray());     // full original line delivered
        Assert.Equal(new[] { "GET 200" }, stdout.Lines.ToArray());
    }

    [Fact]
    public void Field_OutOfRange_IsUnmatched()
    {
        var s1 = new FakeSink("x");
        var stdout = new FakeSink("stdout");
        var routes = new List<(RouteSpec, ISink)> { (Route(".*"), s1) };

        new Router().Run(new StringReader("oneword\n"), Opts(field: 5), routes, null, stdout);

        Assert.Empty(s1.Lines);
        Assert.Equal(new[] { "oneword" }, stdout.Lines.ToArray());
    }
}
```

- [ ] **Step 2: Run tests — expect fail (no Router)**

Run: `dotnet test tests/Winix.Demux.Tests --filter "FullyQualifiedName~RouterTests"`
Expected: FAIL (compile error — Router not defined).

- [ ] **Step 3: Implement `Router.cs`**

```csharp
using System.Collections.Generic;
using System.IO;

namespace Winix.Demux;

/// <summary>Core routing loop. Streams the input one line at a time and dispatches each line to the
/// matching route sink(s); unmatched lines go to the default sink or the stdout passthrough.</summary>
public sealed class Router
{
    private static readonly char[] Whitespace = { ' ', '\t' };

    /// <summary>Reads <paramref name="input"/> to EOF, routing each line. Sinks are injected and
    /// owned by the caller (the caller closes them and reads their counters afterwards).</summary>
    public void Run(
        TextReader input,
        DemuxOptions options,
        IReadOnlyList<(RouteSpec Spec, ISink Sink)> routes,
        ISink? defaultSink,
        ISink stdoutSink)
    {
        string? line;
        while ((line = input.ReadLine()) is not null)
        {
            string subject = Subject(line, options);
            bool matchedAny = false;

            foreach (var (spec, sink) in routes)
            {
                if (spec.Predicate!.IsMatch(subject))
                {
                    sink.Write(line); // always the full original line
                    matchedAny = true;
                    if (!options.All) { break; } // first-match
                }
            }

            if (!matchedAny)
            {
                (defaultSink ?? stdoutSink).Write(line);
            }
        }
    }

    /// <summary>The text the predicate is tested against: a chosen field, or the whole line.</summary>
    private static string Subject(string line, DemuxOptions options)
    {
        if (options.Field is not int n) { return line; }

        string[] parts = options.Delimiter.Length == 0
            ? line.Split(Whitespace, System.StringSplitOptions.RemoveEmptyEntries)
            : line.Split(options.Delimiter);

        return (n >= 1 && n <= parts.Length) ? parts[n - 1] : ""; // out-of-range → empty → unmatched (unless regex matches "")
    }
}
```

> **Verify-at-implementation note:** out-of-range field returns `""`; a route whose regex matches the
> empty string (e.g. `.*` or `^$`) would then match. The `Field_OutOfRange_IsUnmatched` test uses
> `.*` and expects no match — so the contract is "out-of-range field never matches," which requires
> returning a sentinel that no predicate matches rather than `""`. Implement by short-circuiting:
> if the field is out of range, skip predicate evaluation entirely (treat as unmatched). Adjust
> `Run` to detect the out-of-range case and go straight to the default/stdout path.

- [ ] **Step 4: Fix the out-of-range contract**

Change `Subject` to return `string?` (null = out-of-range) and in `Run`, when subject is null, skip
the route loop and deliver to default/stdout:
```csharp
string? subject = Subject(line, options);
bool matchedAny = false;
if (subject is not null)
{
    foreach (var (spec, sink) in routes)
    {
        if (spec.Predicate!.IsMatch(subject))
        {
            sink.Write(line);
            matchedAny = true;
            if (!options.All) { break; }
        }
    }
}
if (!matchedAny) { (defaultSink ?? stdoutSink).Write(line); }
```
and `Subject` returns `null` when `n < 1 || n > parts.Length`.

- [ ] **Step 5: Run tests — expect pass**

Run: `dotnet test tests/Winix.Demux.Tests --filter "FullyQualifiedName~RouterTests"`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add src/Winix.Demux/Router.cs tests/Winix.Demux.Tests/RouterTests.cs
git commit -m "feat(demux): Router — first-match/--all, field-scoped predicate, unmatched→default/stdout"
```

---

## Task 6: RoutingSummary (counts + exit-code precedence + rendering)

**Files:**
- Create: `src/Winix.Demux/RoutingSummary.cs`
- Test: `tests/Winix.Demux.Tests/RoutingSummaryTests.cs`

- [ ] **Step 1: Write `RoutingSummaryTests.cs`**

```csharp
#nullable enable
using System.Collections.Generic;
using Winix.Demux;
using Xunit;

namespace Winix.Demux.Tests;

public class RoutingSummaryTests
{
    private static ISink Sink(string label, long delivered, long undelivered, bool dead, int? exit = null)
    {
        var f = new FakeSink(label) { ChildExitCode = exit };
        for (long i = 0; i < delivered; i++) { f.Write("x"); }
        if (dead) { typeof(FakeSink).GetProperty("IsDead")!.SetValue(f, true); } // test-only force
        return f;
    }

    [Fact]
    public void ExitCode_AllDelivered_IsZero()
    {
        var s = new List<ISink> { new FakeSink("a") };
        var summary = new RoutingSummary(s, exitOnChildError: false);
        Assert.Equal(0, summary.ExitCode);
    }

    [Fact]
    public void ExitCode_Undelivered_IsOne()
    {
        var dead = new FakeSink("a", dieAfter: 0);
        dead.Write("x"); // becomes undelivered
        var summary = new RoutingSummary(new List<ISink> { dead }, exitOnChildError: false);
        Assert.Equal(1, summary.ExitCode);
    }

    [Fact]
    public void ExitCode_ChildNonZeroUnderStrict_IsTwo()
    {
        var s = new FakeSink("a") { ChildExitCode = 3 };
        s.Write("x");
        var summary = new RoutingSummary(new List<ISink> { s }, exitOnChildError: true);
        Assert.Equal(2, summary.ExitCode);
    }

    [Fact]
    public void ExitCode_ChildNonZeroWithoutStrict_IsZero()
    {
        var s = new FakeSink("a") { ChildExitCode = 3 };
        s.Write("x");
        var summary = new RoutingSummary(new List<ISink> { s }, exitOnChildError: false);
        Assert.Equal(0, summary.ExitCode);
    }

    [Fact]
    public void ExitCode_UndeliveredAndChildError_PrecedenceIsOne()
    {
        var dead = new FakeSink("a", dieAfter: 0) { ChildExitCode = 5 };
        dead.Write("x"); // undelivered AND non-zero child
        var summary = new RoutingSummary(new List<ISink> { dead }, exitOnChildError: true);
        Assert.Equal(1, summary.ExitCode); // 1 (data loss) wins over 2
    }
}
```

> **Verify-at-implementation note:** the `Sink` helper using reflection to force `IsDead` is brittle;
> prefer the `FakeSink(dieAfter: 0)` + `Write` pattern shown in the actual tests (which sets `IsDead`
> through the real code path). Drop the reflection helper.

- [ ] **Step 2: Run tests — expect fail**

Run: `dotnet test tests/Winix.Demux.Tests --filter "FullyQualifiedName~RoutingSummaryTests"`
Expected: FAIL (RoutingSummary not defined).

- [ ] **Step 3: Implement `RoutingSummary.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using Yort.ShellKit;

namespace Winix.Demux;

/// <summary>Aggregates per-sink outcomes into the exit code and the human/JSON summary.</summary>
public sealed class RoutingSummary
{
    private readonly IReadOnlyList<ISink> _sinks;
    private readonly bool _exitOnChildError;

    public RoutingSummary(IReadOnlyList<ISink> sinks, bool exitOnChildError)
    {
        _sinks = sinks;
        _exitOnChildError = exitOnChildError;
    }

    /// <summary>0 = all delivered; 1 = partial delivery failure (data lost); 2 = watched child
    /// exited non-zero under --exit-on-child-error. 1 takes precedence over 2.</summary>
    public int ExitCode
    {
        get
        {
            bool anyUndelivered = _sinks.Any(s => s.UndeliveredCount > 0);
            if (anyUndelivered) { return 1; }
            if (_exitOnChildError && _sinks.Any(s => s.ChildExitCode is int c && c != 0)) { return 2; }
            return 0;
        }
    }

    /// <summary>Renders the human-readable summary (per-sink counts, dead routes, child exits).</summary>
    public string FormatHuman(bool useColor)
    {
        var lines = new List<string>();
        foreach (ISink s in _sinks)
        {
            string status = s.IsDead ? $" [DEAD, {s.UndeliveredCount} undelivered]" : "";
            string child = s.ChildExitCode is int c ? $" (child exit {c})" : "";
            lines.Add($"  {s.Label}: {s.DeliveredCount} delivered{status}{child}");
        }
        return "demux summary:\n" + string.Join("\n", lines);
    }

    /// <summary>Renders the summary as a JSON envelope (written to stderr).</summary>
    public string FormatJson(string toolName, string version)
    {
        var (w, buffer) = JsonHelper.CreateWriter();
        using (w)
        {
            w.WriteStartObject();
            w.WriteString("tool", toolName);
            w.WriteString("version", version);
            w.WriteNumber("exit_code", ExitCode);
            w.WriteString("exit_reason", ExitCode switch
            {
                0 => "success",
                1 => "partial_delivery_failure",
                2 => "watched_child_failed",
                _ => "error",
            });
            w.WriteStartArray("routes");
            foreach (ISink s in _sinks)
            {
                w.WriteStartObject();
                w.WriteString("label", s.Label);
                w.WriteNumber("delivered", s.DeliveredCount);
                w.WriteNumber("undelivered", s.UndeliveredCount);
                w.WriteBoolean("dead", s.IsDead);
                if (s.ChildExitCode is int c) { w.WriteNumber("child_exit_code", c); }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return JsonHelper.GetString(buffer);
    }
}
```

> **Verify-at-implementation note:** confirm `JsonHelper.CreateWriter()`/`GetString(buffer)` signatures
> against `src/Yort.ShellKit/JsonHelper.cs` (the `ParseResult.FormatUsageErrorJson` method uses exactly
> this shape, so it is correct, but re-check the tuple deconstruction names).

- [ ] **Step 4: Run tests — expect pass** (remove the reflection helper; use `FakeSink(dieAfter:0)`)

Run: `dotnet test tests/Winix.Demux.Tests --filter "FullyQualifiedName~RoutingSummaryTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Demux/RoutingSummary.cs tests/Winix.Demux.Tests/RoutingSummaryTests.cs
git commit -m "feat(demux): RoutingSummary — exit-code precedence (1>2) + human/JSON rendering"
```

---

## Task 7: CommandSink (integration — real child processes)

**Files:**
- Create: `src/Winix.Demux/CommandSink.cs`
- Test: `tests/Winix.Demux.Tests/IntegrationTests_CommandSink.cs`

CommandSink spawns the target command **once** via the platform shell, feeds matching lines to its
stdin, and on `Close` closes stdin, waits, and records the exit code. Per the protocol-fake caution
in `CLAUDE.md`, tests use **real** child processes — wire correctness (stdin delivery, broken pipe)
only shows against a real process.

> **Concurrency model (adversarial-review F1/F2 — load-bearing):** A single thread feeding N
> children's stdin with blocking writes deadlocks: a child that stalls (commonly one writing to
> demux's *inherited* stdout while that's backpressured) stops reading its stdin, its pipe buffer
> fills, and the feeding thread blocks forever — starving every sibling. Therefore **each
> `CommandSink` owns a background writer thread draining a bounded `BlockingCollection<string>`**,
> so one slow/stuck child cannot block the router or its siblings (it only applies backpressure on
> its own queue). Broken-pipe detection moves into the writer thread. `Close` completes the queue,
> joins the writer (bounded), closes stdin, then `WaitForExit(timeout)`; on timeout it kills the
> process tree and records a sentinel exit code so a hung child can never hang demux.
>
> Residual limitation (document, don't fix in v1): a child that is *alive but never reads* while
> demux's downstream stdout is also stalled will eventually fill its queue and apply backpressure —
> correct behaviour for a stalled pipeline, but it means a deliberately-broken child can stall the
> run. Covered by the `WaitForExit` timeout only at shutdown, not mid-stream. Noted in the ADR.

- [ ] **Step 1: Implement `CommandSink.cs`**

```csharp
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Winix.Demux;

/// <summary>Command target. Spawns the command once via the platform shell at construction and
/// drains a bounded queue to its stdin on a dedicated writer thread, so one slow or stuck child
/// cannot block the router or its siblings. Broken-pipe-safe (a dead child marks the sink dead and
/// counts the rest undelivered). Captures the child's exit code on Close, killing it on timeout.</summary>
public sealed class CommandSink : ISink
{
    private const int QueueCapacity = 1024;

    private readonly TimeSpan _exitTimeout;
    private readonly Process _process;
    private readonly StreamWriter _stdin;
    private readonly BlockingCollection<string> _queue = new(QueueCapacity);
    private readonly Thread _writer;
    private long _delivered;
    private long _undelivered;
    private volatile bool _dead;

    /// <summary>Spawns the command. Throws if the shell process cannot start (caller maps to 126).
    /// <paramref name="exitTimeout"/> (default 10s) bounds shutdown — it is an injectable seam so tests
    /// can drive the hung-child kill path deterministically without a real 10s wait.</summary>
    public CommandSink(string command, string label, TimeSpan? exitTimeout = null)
    {
        Label = label;
        _exitTimeout = exitTimeout ?? TimeSpan.FromSeconds(10);
        var psi = new ProcessStartInfo { RedirectStandardInput = true, UseShellExecute = false };
        if (OperatingSystem.IsWindows())
        {
            psi.FileName = "cmd.exe";
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(command);
        }
        else
        {
            psi.FileName = "/bin/sh";
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);
        }
        _process = Process.Start(psi) ?? throw new IOException($"could not start: {command}");
        _stdin = _process.StandardInput;

        _writer = new Thread(DrainQueue) { IsBackground = true, Name = $"demux-sink:{label}" };
        _writer.Start();
    }

    public string Label { get; }
    public long DeliveredCount => Interlocked.Read(ref _delivered);
    public long UndeliveredCount => Interlocked.Read(ref _undelivered);
    public bool IsDead => _dead;
    public int? ChildExitCode { get; private set; }

    /// <summary>Enqueues a line for the writer thread. Blocks only if THIS sink's queue is full
    /// (its own backpressure); never blocks on a sibling. Counts undelivered once the sink is dead.</summary>
    public void Write(string line)
    {
        if (_dead) { Interlocked.Increment(ref _undelivered); return; }
        try { _queue.Add(line); }
        catch (InvalidOperationException) { Interlocked.Increment(ref _undelivered); } // adding completed
    }

    private void DrainQueue()
    {
        try
        {
            foreach (string line in _queue.GetConsumingEnumerable())
            {
                try { _stdin.Write(line); _stdin.Write('\n'); Interlocked.Increment(ref _delivered); }
                catch (IOException)
                {
                    // F2: the line in hand was dequeued but never written — count it undelivered here
                    // (the finally only drains lines STILL queued), else a lost in-flight line would be
                    // invisible to the exit code and report a false success.
                    Interlocked.Increment(ref _undelivered);
                    _dead = true;
                    break;   // child closed stdin / exited
                }
            }
        }
        finally
        {
            // Anything still queued (or arriving) after death is undelivered.
            while (_queue.TryTake(out _)) { Interlocked.Increment(ref _undelivered); }
        }
    }

    public void Close()
    {
        _queue.CompleteAdding();

        // F1: the writer may be blocked in _stdin.Write (child alive, not reading, pipe full) — an
        // unconditional Join() would hang forever and a timeout placed AFTER it would never run. So
        // bound the writer-drain with a timeout and KILL on overrun, which makes the blocked write
        // throw and lets the writer thread exit. The kill must precede the final unconditional Join.
        if (!_writer.Join(_exitTimeout))
        {
            try { _process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            _writer.Join();                       // bounded now: the kill unblocked the write
            try { _stdin.Close(); } catch (IOException) { }
            try { _process.WaitForExit(); } catch { }
            ChildExitCode = -1;                   // sentinel: killed after timeout
            _process.Dispose();
            return;
        }

        // Writer drained cleanly; signal EOF and wait (bounded) for the child to exit on its own.
        try { _stdin.Close(); } catch (IOException) { /* already gone */ }
        if (_process.WaitForExit((int)_exitTimeout.TotalMilliseconds))
        {
            ChildExitCode = _process.ExitCode;
        }
        else
        {
            try { _process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            try { _process.WaitForExit(); } catch { }
            ChildExitCode = -1;                   // sentinel: killed after timeout
        }
        _process.Dispose();
    }
}
```

> **Verify-at-implementation note:** count any line that was queued but never written (child died
> mid-drain) as undelivered — the `finally` drains the queue; also ensure lines still *arriving* via
> `Write` after `_dead` are counted (the `_dead` guard in `Write` handles that). The `ChildExitCode
> == -1` sentinel must be surfaced distinctly in the summary ("killed after timeout").

- [ ] **Step 2: Write `IntegrationTests_CommandSink.cs`**

```csharp
#nullable enable
using System.IO;
using Winix.Demux;
using Xunit;

namespace Winix.Demux.Tests;

public class IntegrationTests_CommandSink
{
    // Cross-platform child: 'cat > file' echoes stdin to a file on both sh and cmd... but cmd has no
    // 'cat'. Use a portable target: on Unix 'cat', on Windows 'findstr "^"' (passes all lines).
    private static string CatToFile(string path)
        => OperatingSystem.IsWindows() ? $"more > \"{path}\"" : $"cat > \"{path}\"";

    [SkippableFact]
    public void Write_DeliversLinesToChildStdin()
    {
        Skip.IfNot(!OperatingSystem.IsWindows(), "uses /bin/sh cat; Windows variant covered separately");
        if (OperatingSystem.IsWindows()) { return; } // CA1416

        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var sink = new CommandSink($"cat > \"{path}\"", "ERROR");
            sink.Write("one");
            sink.Write("two");
            sink.Close();

            string content = File.ReadAllText(path);
            // F3: do NOT normalise \r\n — cat is byte-faithful, so this verifies demux fed explicit \n
            // (a regression to WriteLine on Windows would emit \r\n and fail this).
            Assert.Equal("one\ntwo\n", content);
            Assert.DoesNotContain("\r", content);
            Assert.Equal(2, sink.DeliveredCount);
            Assert.Equal(0, sink.ChildExitCode);
        }
        finally { File.Delete(path); }
    }

    [SkippableFact]
    public void Close_HungChild_KilledAfterTimeout_SetsSentinelAndReturnsBounded()
    {
        Skip.IfNot(!OperatingSystem.IsWindows(), "sh child");
        if (OperatingSystem.IsWindows()) { return; }

        // F4 / D11: child ignores stdin and never exits. With a short injected timeout, Close must
        // still return promptly, kill the child, and record the -1 sentinel.
        var sink = new CommandSink("sleep 60", "x", exitTimeout: TimeSpan.FromMilliseconds(300));
        sink.Write("a");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        sink.Close();
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(9), $"Close did not honour the timeout ({sw.Elapsed})");
        Assert.Equal(-1, sink.ChildExitCode);
    }

    [SkippableFact]
    public void MidStreamDeath_ConservesLineCount()
    {
        Skip.IfNot(!OperatingSystem.IsWindows(), "sh child");
        if (OperatingSystem.IsWindows()) { return; }

        // F2 regression: delivered + undelivered must equal lines written even when the child dies
        // mid-drain and the in-flight line fails — no line may vanish from both counters.
        var sink = new CommandSink("head -n1 >/dev/null", "x", exitTimeout: TimeSpan.FromSeconds(2));
        const int written = 500;
        for (int i = 0; i < written; i++) { sink.Write("line-" + i); }
        sink.Close();

        Assert.Equal(written, sink.DeliveredCount + sink.UndeliveredCount);
        Assert.True(sink.UndeliveredCount > 0);
    }

    [SkippableFact]
    public void Write_ChildExitsEarly_MarksDeadCountsUndeliveredKeepsRunning()
    {
        Skip.IfNot(!OperatingSystem.IsWindows(), "sh child");
        if (OperatingSystem.IsWindows()) { return; }

        // 'head -n1' consumes one line then exits, closing the pipe.
        var sink = new CommandSink("head -n1 >/dev/null", "x");
        sink.Write("first");
        // give the child a moment to exit; subsequent writes hit a broken pipe
        for (int i = 0; i < 1000 && !sink.IsDead; i++) { sink.Write("flood-" + i); }
        sink.Close();

        Assert.True(sink.IsDead);
        Assert.True(sink.UndeliveredCount > 0);
    }

    [SkippableFact]
    public void Close_CapturesNonZeroChildExit()
    {
        Skip.IfNot(!OperatingSystem.IsWindows(), "sh child");
        if (OperatingSystem.IsWindows()) { return; }

        var sink = new CommandSink("exit 3", "x");
        sink.Close();
        Assert.Equal(3, sink.ChildExitCode);
    }
}
```

> **Verify-at-implementation note:** the broken-pipe test is inherently timing-dependent (the child
> must exit before the flood writes). The 1000-iteration flood with an `IsDead` early-out is a
> pragmatic bound, not a guarantee; if it proves flaky in CI, pin it by `WaitForExit`-ing a probe or
> writing a larger payload. Add a matching Windows variant (`more`/`findstr`) once the Unix path is
> green; both are `SkippableFact` + `Skip.IfNot` + redundant `if return` for CA1416 per the suite rule.

- [ ] **Step 3: Run tests (on Unix) — expect pass**

Run: `dotnet test tests/Winix.Demux.Tests --filter "FullyQualifiedName~IntegrationTests_CommandSink"`
Expected: PASS on Linux/macOS; SKIPPED on Windows for the sh-only cases.

- [ ] **Step 4: Add the Windows (`cmd /c`) CommandSink variant — REQUIRED, not optional (F5)**

demux's core justification is the Windows pipe-filter gap (ADR D1), and `cmd /c` stdin/pipe semantics
differ materially from `sh -c`. Add Windows-gated mirrors of delivery, broken-pipe (early-exit), and
non-zero-exit so the process-sink contract is verified on Windows, not just Unix. Use a `cmd`-portable
child — delivery via `findstr "^" > "<path>"` (passes all lines) or `more > "<path>"`; non-zero exit
via `cmd /c "exit /b 3"`. Each test: `[SkippableFact]` + `Skip.IfNot(OperatingSystem.IsWindows(), ...)`
+ redundant `if (!OperatingSystem.IsWindows()) return;` for CA1416. Verify byte output without masking
`\r` only where the child is byte-faithful (note `findstr`/`more` may rewrite terminators — if so, assert
on demux's stdin feed via a capture child rather than the file).

- [ ] **Step 5: Run the Windows variant (on a Windows host / CI) — expect pass**

Run (Windows): `dotnet test tests/Winix.Demux.Tests --filter "FullyQualifiedName~IntegrationTests_CommandSink"`
Expected: the Windows-gated cases PASS; the sh-only cases SKIPPED.

- [ ] **Step 6: Commit**

```bash
git add src/Winix.Demux/CommandSink.cs tests/Winix.Demux.Tests/IntegrationTests_CommandSink.cs
git commit -m "feat(demux): CommandSink — writer-thread feed, broken-pipe-safe, bounded shutdown+kill, exit capture"
```

---

## Task 8: ArgParser (custom route scan + ShellKit residual parse)

**Files:**
- Create: `src/Winix.Demux/ArgParser.cs`
- Test: `tests/Winix.Demux.Tests/ArgParserTests.cs`

The parser does a custom pre-pass to peel route flags + operands out of argv, then hands the residual
(the 0/1-arg flags + standard flags) to ShellKit. `BuildParser` documents the route flags in help via
a `.Section()` even though they're parsed manually.

- [ ] **Step 1: Write `ArgParserTests.cs`** (covers the scan, validation → 125, regex compile)

```csharp
#nullable enable
using System.IO;
using Winix.Demux;
using Xunit;

namespace Winix.Demux.Tests;

public class ArgParserTests
{
    private static (DemuxOptions? opts, string err, int code) Parse(params string[] args)
    {
        var stderr = new StringWriter();
        var parser = ArgParser.BuildParser("0.0.0");
        int r = ArgParser.TryParse(args, parser, stderr, out DemuxOptions? opts, out _);
        return (opts, stderr.ToString(), r);
    }

    [Fact]
    public void TwoRoutes_FileAndExec_Parsed()
    {
        var (opts, _, code) = Parse("--to", "ERROR", "err.log", "--exec", "WARN", "logger");
        Assert.Equal(0, code);
        Assert.NotNull(opts);
        Assert.Equal(2, opts!.Routes.Count);
        Assert.Equal(TargetKind.File, opts.Routes[0].Kind);
        Assert.Equal("err.log", opts.Routes[0].Target);
        Assert.Equal(TargetKind.Exec, opts.Routes[1].Kind);
    }

    [Fact]
    public void Default_Parsed()
    {
        var (opts, _, code) = Parse("--to", "E", "e.log", "--default-exec", "gzip > r.gz");
        Assert.Equal(0, code);
        Assert.NotNull(opts!.DefaultRoute);
        Assert.Equal(TargetKind.Exec, opts.DefaultRoute!.Kind);
    }

    [Fact]
    public void NoRoutes_IsUsageError()
    {
        var (opts, err, code) = Parse("--all");
        Assert.Equal(125, code);
        Assert.Null(opts);
        Assert.Contains("no routes", err);
    }

    [Fact]
    public void RouteFlagMissingOperand_IsUsageError()
    {
        var (_, err, code) = Parse("--to", "ERROR"); // missing FILE
        Assert.Equal(125, code);
        Assert.Contains("operand", err);
    }

    [Fact]
    public void TwoDefaults_IsUsageError()
    {
        var (_, err, code) = Parse("--to", "E", "e.log", "--default-to", "a", "--default-exec", "b");
        Assert.Equal(125, code);
    }

    [Fact]
    public void BadRegex_IsUsageError()
    {
        var (_, err, code) = Parse("--to", "(unclosed", "e.log");
        Assert.Equal(125, code);
    }

    [Fact]
    public void FieldZero_IsUsageError()
    {
        var (_, _, code) = Parse("--to", "E", "e.log", "--field", "0");
        Assert.Equal(125, code);
    }

    [Fact]
    public void Field_And_Flags_ReadFromResidual()
    {
        var (opts, _, code) = Parse("--to", "E", "e.log", "--field", "3", "--all", "--append");
        Assert.Equal(0, code);
        Assert.Equal(3, opts!.Field);
        Assert.True(opts.All);
        Assert.True(opts.Append);
    }
}
```

- [ ] **Step 2: Run — expect fail (no ArgParser)**

Run: `dotnet test tests/Winix.Demux.Tests --filter "FullyQualifiedName~ArgParserTests"`
Expected: FAIL (compile error).

- [ ] **Step 3: Implement `ArgParser.cs`**

```csharp
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Yort.ShellKit;

namespace Winix.Demux;

/// <summary>Parses demux arguments. Route flags (--to/--exec/--default-*) carry their operands and
/// are peeled out of argv by a custom pre-pass; the residual flags go through ShellKit.</summary>
public static class ArgParser
{
    private const int UsageError = 125; // ExitCode.UsageError

    /// <summary>Returns 0 and sets <paramref name="opts"/> on success; on a usage error writes the
    /// message to <paramref name="stderr"/> and returns 125. Sets <paramref name="handled"/> true
    /// (and returns the handled exit code) when the pre-built <paramref name="parser"/> handled
    /// --help/--version/--describe. The parser is built once by <see cref="Cli"/> with the real
    /// version and threaded in, so there is no second parser build here.</summary>
    public static int TryParse(string[] args, CommandLineParser parser, TextWriter stderr,
        out DemuxOptions? opts, out bool handled)
    {
        opts = null;
        handled = false;

        if (!ScanRoutes(args, out List<RawRoute> rawRoutes, out RawRoute? rawDefault,
                        out List<string> residual, out string? scanError))
        {
            stderr.WriteLine($"demux: {scanError}");
            return UsageError;
        }

        ParseResult result = parser.Parse(residual.ToArray());
        if (result.IsHandled) { handled = true; return result.ExitCode; }
        if (result.HasErrors) { return result.WriteErrors(stderr); }

        if (rawRoutes.Count == 0)
        {
            stderr.WriteLine("demux: no routes — give at least one --to PATTERN FILE or --exec PATTERN CMD");
            return UsageError;
        }

        int? field = result.Has("--field") ? result.GetInt("--field") : null;
        if (field is int f && f < 1)
        {
            stderr.WriteLine("demux: --field must be >= 1");
            return UsageError;
        }

        var routes = new List<RouteSpec>(rawRoutes.Count);
        foreach (RawRoute r in rawRoutes)
        {
            if (!TryCompile(r.Pattern, stderr, out Regex? rx)) { return UsageError; }
            routes.Add(new RouteSpec(rx!, r.Pattern, r.Kind, r.Target));
        }

        RouteSpec? defaultRoute = rawDefault is RawRoute d
            ? RouteSpec.Default(d.Kind, d.Target)
            : null;

        opts = new DemuxOptions(
            routes,
            defaultRoute,
            field,
            result.Has("--delimiter") ? result.GetString("--delimiter") : "",
            result.Has("--all"),
            result.Has("--append"),
            result.Has("--exit-on-child-error"),
            result.Has("--json"),
            result.ResolveColor(checkStdErr: true));
        return 0;
    }

    private readonly struct RawRoute
    {
        public RawRoute(TargetKind kind, string pattern, string target) { Kind = kind; Pattern = pattern; Target = target; }
        public TargetKind Kind { get; }
        public string Pattern { get; }  // "" for default
        public string Target { get; }
    }

    private static bool ScanRoutes(
        string[] args, out List<RawRoute> routes, out RawRoute? def,
        out List<string> residual, out string? error)
    {
        routes = new List<RawRoute>();
        residual = new List<string>();
        def = null;
        error = null;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "--to":
                case "--exec":
                {
                    TargetKind kind = a == "--to" ? TargetKind.File : TargetKind.Exec;
                    if (i + 2 >= args.Length)
                    {
                        error = $"{a} requires two operands: PATTERN and " + (kind == TargetKind.File ? "FILE" : "CMD");
                        return false;
                    }
                    routes.Add(new RawRoute(kind, args[i + 1], args[i + 2]));
                    i += 2;
                    break;
                }
                case "--default-to":
                case "--default-exec":
                {
                    if (def is not null) { error = "at most one --default-to/--default-exec may be given"; return false; }
                    TargetKind kind = a == "--default-to" ? TargetKind.File : TargetKind.Exec;
                    if (i + 1 >= args.Length) { error = $"{a} requires an operand"; return false; }
                    def = new RawRoute(kind, "", args[i + 1]);
                    i += 1;
                    break;
                }
                default:
                    residual.Add(a);
                    break;
            }
        }
        return true;
    }

    private static bool TryCompile(string pattern, TextWriter stderr, out Regex? rx)
    {
        try { rx = SafeRegex.Create(pattern, RegexOptions.None); return true; }
        catch (System.Exception ex)
        {
            stderr.WriteLine($"demux: invalid regex '{pattern}': {ex.Message}");
            rx = null;
            return false;
        }
    }

    /// <summary>Builds the parser for the residual flags + standard flags, and documents the route
    /// flags (parsed manually) in --help/--describe.</summary>
    public static CommandLineParser BuildParser(string version)
    {
        return new CommandLineParser("demux", version)
            .Description("Route each line of stdin to files or commands by regex; unmatched lines pass through to stdout.")
            .StandardFlags()
            .Option("--field", null, "N", "Test the regex against column N (1-based) instead of the whole line")
            .Option("--delimiter", null, "CHAR", "Field delimiter (default: runs of whitespace)")
            .Flag("--all", null, "Broadcast: route to every matching route (default: first-match)")
            .Flag("--append", null, "File targets append instead of truncate")
            .Flag("--exit-on-child-error", null, "A watched child's non-zero exit makes demux exit 2")
            .Section("Routes",
                "--to PATTERN FILE     Route lines matching regex PATTERN to FILE (repeatable).\n" +
                "--exec PATTERN CMD    Route matching lines to a command's stdin (shell-spawned, repeatable).\n" +
                "--default-to FILE     Unmatched records -> FILE.\n" +
                "--default-exec CMD    Unmatched records -> a command. (Omit both -> unmatched -> stdout.)\n" +
                "PATTERN is a bare .NET regex (not slash-delimited). Quote it to protect the shell.")
            .ExitCodes(
                (0, "Success — all input routed and delivered"),
                (1, "Partial delivery failure — a route died, records undelivered"),
                (2, "Watched child exited non-zero (--exit-on-child-error)"),
                (125, "Usage error"),
                (126, "Setup failure — could not open a --to file or launch the shell"))
            .Example("cat app.log | demux --to ERROR err.log --default-exec 'gzip > rest.gz'",
                     "Split errors into a file, compress the rest")
            .ComposesWith("peep", "peep 'demux ... ' ", "Re-run a routing pipeline on change")
            .JsonField("tool", "string", "Tool name (\"demux\")")
            .JsonField("exit_code", "int", "0/1/2 — see exit codes")
            .JsonField("routes", "array", "Per-route delivered/undelivered/dead/child_exit_code");
    }
}
```

> **Verify-at-implementation notes:**
> 1. Confirm `.Option`/`.Flag`/`.Section`/`.ExitCodes`/`.Example`/`.ComposesWith`/`.JsonField`
>    signatures against `src/Yort.ShellKit/CommandLineParser.cs` (they match `when`/`mksecret` usage,
>    so they are correct, but re-check the `--field`/`--delimiter` `Option` arity — `Option(long,
>    short, placeholder, description)`).
> 2. `ExitCode.UsageError` is `125` (per `src/Yort.ShellKit/ExitCode.cs`); use that constant in place
>    of the `UsageError` literal here and the `126` literal in `Cli` (`ExitCode` for setup failure).

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test tests/Winix.Demux.Tests --filter "FullyQualifiedName~ArgParserTests"`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Demux/ArgParser.cs tests/Winix.Demux.Tests/ArgParserTests.cs
git commit -m "feat(demux): ArgParser — custom 2-operand route scan + ShellKit residual parse + validation"
```

---

## Task 9: Cli.Run orchestrator

**Files:**
- Create: `src/Winix.Demux/Cli.cs`
- Test: `tests/Winix.Demux.Tests/CliTests.cs`

`Cli.Run(args, stdin, stdout, stderr)` is the seam (note the extra `stdin` vs the 3-arg seam used by
non-filter tools — demux consumes stdin). It: builds the parser with the real version, parses, builds
sinks (mapping construction failures to 126), runs the Router, closes sinks, emits the summary to
stderr, and returns the exit code.

- [ ] **Step 1: Write `CliTests.cs`** (focus on arg/exit-code paths with small in-memory stdin; route-to-file so no child processes)

```csharp
#nullable enable
using System.IO;
using Winix.Demux;
using Xunit;

namespace Winix.Demux.Tests;

public class CliTests
{
    private static (int code, string outText, string errText) Run(string stdin, params string[] args)
    {
        var so = new StringWriter();
        var se = new StringWriter();
        int code = Cli.Run(args, new StringReader(stdin), so, se);
        return (code, so.ToString(), se.ToString());
    }

    [Fact]
    public void RoutesMatchingLinesToFile_UnmatchedToStdout()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var (code, outText, _) = Run("ERROR a\nplain b\n", "--to", "ERROR", path);
            Assert.Equal(0, code);
            Assert.Equal("plain b\n", outText.Replace("\r\n", "\n"));   // unmatched passthrough
            Assert.Equal("ERROR a\n", File.ReadAllText(path).Replace("\r\n", "\n"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NoRoutes_Exits125()
    {
        var (code, _, err) = Run("x\n", "--all");
        Assert.Equal(125, code);
        Assert.Contains("no routes", err);
    }

    [Fact]
    public void UnopenableFile_Exits126()
    {
        string bad = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "x.log"); // missing dir
        var (code, _, _) = Run("ERROR\n", "--to", "ERROR", bad);
        Assert.Equal(126, code);
    }

    [Fact]
    public void Help_Exits0()
    {
        var (code, _, _) = Run("", "--help");
        Assert.Equal(0, code);
    }
}
```

- [ ] **Step 2: Run — expect fail**

Run: `dotnet test tests/Winix.Demux.Tests --filter "FullyQualifiedName~CliTests"`
Expected: FAIL (no Cli).

- [ ] **Step 3: Implement `Cli.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Yort.ShellKit;

namespace Winix.Demux;

/// <summary>Library entry point. Program.cs is a thin shim around <see cref="Run"/>.</summary>
public static class Cli
{
    /// <summary>Parse, build sinks, route stdin, summarise, return exit code. The extra
    /// <paramref name="stdin"/> parameter (vs the 3-arg seam) reflects that demux is an input filter.</summary>
    public static int Run(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr)
    {
        string version = GetVersion();
        var parser = ArgParser.BuildParser(version);

        int parseCode = ArgParser.TryParse(args, parser, stderr, out DemuxOptions? opts, out bool handled);
        if (handled || parseCode != 0) { return parseCode; }
        DemuxOptions options = opts!;

        // PRE-FLIGHT (adversarial-review F3): probe every FILE target for writability WITHOUT
        // truncating, before constructing any truncating sink. Otherwise opening file #1 (truncate)
        // then failing to open file #3 would destroy file #1's contents though demux processed
        // nothing. If any probe fails → 126 with nothing truncated.
        if (!PreflightFileTargets(options, stderr, out int preflightCode))
        {
            return preflightCode; // 126
        }

        // Build sinks. File opens already validated above; a shell-spawn failure is still possible → 126.
        var routeSinks = new List<(RouteSpec, ISink)>(options.Routes.Count);
        ISink? defaultSink = null;
        var stdoutSink = new StdoutSink(stdout);
        var allSinks = new List<ISink>();
        try
        {
            foreach (RouteSpec r in options.Routes)
            {
                ISink sink = MakeSink(r, options.Append);
                routeSinks.Add((r, sink));
                allSinks.Add(sink);
            }
            if (options.DefaultRoute is RouteSpec dr)
            {
                defaultSink = MakeSink(dr, options.Append);
                allSinks.Add(defaultSink);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            foreach (ISink s in allSinks) { try { s.Close(); } catch { /* best effort */ } }
            stderr.WriteLine($"demux: setup failure: {ex.Message}");
            return 126;
        }

        allSinks.Add(stdoutSink);

        new Router().Run(stdin, options, routeSinks, defaultSink, stdoutSink);
        foreach (ISink s in allSinks) { s.Close(); }

        var summary = new RoutingSummary(allSinks, options.ExitOnChildError);
        int exit = summary.ExitCode; // computed from sink counters before any formatting can throw
        // Category-9 hardening: a formatting failure must never turn a correct run into a crash with
        // no exit code. Emit best-effort; the exit code is already decided from the data path.
        try { stderr.WriteLine(options.Json ? summary.FormatJson("demux", version) : summary.FormatHuman(options.UseColor)); }
        catch (Exception ex) { try { stderr.WriteLine($"demux: (summary unavailable: {ex.Message})"); } catch { /* give up */ } }
        return exit;
    }

    private static ISink MakeSink(RouteSpec r, bool append) => r.Kind switch
    {
        TargetKind.File => new FileSink(r.Target, r.PatternText, append),
        TargetKind.Exec => new CommandSink(r.Target, r.PatternText),
        _ => throw new InvalidOperationException(),
    };

    /// <summary>Probes every File target for writability WITHOUT truncating (FileMode.OpenOrCreate),
    /// so a later open-failure can't destroy an earlier file's contents. Returns false + sets 126
    /// on the first unopenable target.</summary>
    private static bool PreflightFileTargets(DemuxOptions options, TextWriter stderr, out int code)
    {
        code = 0;
        IEnumerable<RouteSpec> fileRoutes = options.Routes.Where(r => r.Kind == TargetKind.File);
        if (options.DefaultRoute is RouteSpec dr && dr.Kind == TargetKind.File)
        {
            fileRoutes = fileRoutes.Append(dr);
        }
        foreach (RouteSpec r in fileRoutes)
        {
            try
            {
                // OpenOrCreate creates a missing file but does NOT truncate an existing one.
                using var probe = new FileStream(r.Target, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                stderr.WriteLine($"demux: setup failure: cannot write '{r.Target}': {ex.Message}");
                code = 126;
                return false;
            }
        }
        return true;
    }

    private static string GetVersion()
    {
        string? v = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(v)) { return "0.0.0"; }
        int plus = v.IndexOf('+');
        return plus >= 0 ? v.Substring(0, plus) : v; // strip +gitsha
    }
}
```

> **Verify-at-implementation note:** this assumes the Task 8 refactor where `ArgParser.TryParse` takes
> the pre-built `parser` and exposes a `handled` out-param (for --help/--version/--describe). Make the
> two tasks' signatures match — `TryParse(string[] args, CommandLineParser parser, TextWriter stderr,
> out DemuxOptions? opts, out bool handled)`. The CommandSink path is exercised by the Task 7
> integration tests, not CliTests, to keep CliTests process-free and deterministic.

- [ ] **Step 4: Run — expect pass**

Run: `dotnet test tests/Winix.Demux.Tests --filter "FullyQualifiedName~CliTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Run the full Demux suite**

Run: `dotnet test tests/Winix.Demux.Tests`
Expected: all green (Unix); CommandSink Windows-only cases skipped as appropriate.

- [ ] **Step 6: Commit**

```bash
git add src/Winix.Demux/Cli.cs tests/Winix.Demux.Tests/CliTests.cs
git commit -m "feat(demux): Cli.Run orchestrator — parse, build sinks (126 on setup), route, summarise"
```

---

## Task 10: Console app (Program.cs)

**Files:**
- Create: `src/demux/Program.cs`

- [ ] **Step 1: Implement `Program.cs`** (thin shim; UTF-8 console per the suite Windows-encoding rule)

```csharp
using System;
using Winix.Demux;
using Yort.ShellKit;

namespace Demux;

internal static class Program
{
    private static int Main(string[] args)
    {
        ConsoleEnv.UseUtf8Streams();
        return Cli.Run(args, Console.In, Console.Out, Console.Error);
    }
}
```

- [ ] **Step 2: Build the console app**

Run: `dotnet build src/demux/demux.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Manual smoke (bash)**

Run:
```bash
printf 'ERROR x\nWARN y\nplain z\n' | dotnet run --project src/demux -- --to ERROR /tmp/e.log --to WARN /tmp/w.log
cat /tmp/e.log /tmp/w.log
```
Expected: stdout shows `plain z`; `/tmp/e.log` has `ERROR x`; `/tmp/w.log` has `WARN y`; stderr shows the summary.

- [ ] **Step 4: Commit**

```bash
git add src/demux/Program.cs
git commit -m "feat(demux): console app entry point"
```

---

## Task 11: Docs — README, man page, AI guide, llms.txt

**Files:**
- Create: `src/demux/README.md`
- Create: `src/demux/man/man1/demux.1`
- Create: `docs/ai/demux.md`
- Modify: `llms.txt`

- [ ] **Step 1: Write `src/demux/README.md`**

Follow the structure of `src/trash/README.md`: description, install (scoop/nuget/AOT), usage with the
worked example from the design §2, an options table (the route flags + `--field`/`--delimiter`/
`--all`/`--append`/`--exit-on-child-error`), the exit-codes table from design §6, the colour/NO_COLOR
section, and a "composes with" example. State that PATTERN is a bare regex.

- [ ] **Step 2: Write `src/demux/man/man1/demux.1`**

Mirror `src/trash/man/man1/trash.1` groff structure (NAME/SYNOPSIS/DESCRIPTION/OPTIONS/EXIT
STATUS/EXAMPLES). SYNOPSIS: `demux [options] --to PATTERN FILE | --exec PATTERN CMD ...`. Document the
2-operand route flags, the bare-regex rule, first-match/--all, unmatched→stdout, and exit codes 0/1/2/125/126.

- [ ] **Step 3: Write `docs/ai/demux.md`**

Mirror `docs/ai/trash.md`: what it is, when an agent should choose it (route a stream to files/commands
by pattern in one pass; the cross-platform readable alternative to awk; real Windows pipe-filter gap),
when NOT to (single-sink filtering → use grep/files), the flag surface, `--json` envelope shape, and the
exit-code contract incl. the 1>2 precedence.

- [ ] **Step 4: Add the `llms.txt` entry**

Add a `demux` line to `llms.txt` matching the existing per-tool format (name, one-liner, key flags).

- [ ] **Step 5: Build (man page packs) + commit**

Run: `dotnet build src/demux/demux.csproj`
Expected: Build succeeded (man page Content Include resolves).
```bash
git add src/demux/README.md src/demux/man docs/ai/demux.md llms.txt
git commit -m "docs(demux): README, man page, AI guide, llms.txt entry"
```

---

## Task 12: Distribution wiring

**Files:**
- Create: `bucket/demux.json`
- Modify: `.github/workflows/release.yml`
- Modify: `.github/workflows/post-publish.yml`
- Modify: `CLAUDE.md`

- [ ] **Step 1: Create `bucket/demux.json`**

Mirror `bucket/trash.json` (scoop manifest), changing name/description/binary to `demux`. Do NOT edit
`bucket/winix.json`.

- [ ] **Step 2: Wire `release.yml`**

Per the CLAUDE.md "adding a new tool" checklist: add a `dotnet publish` step per `matrix.rid`, a
`dotnet pack` step, per-tool zip steps (Linux/macOS + Windows), the combined-zip `Copy-Item`, and the
`tools: { … }` map entry — all mirroring the `trash`/`hcat` entries.

- [ ] **Step 3: Wire `post-publish.yml`**

Add `update_manifest bucket/demux.json …` and `generate_manifests "demux" "Demux" "Route each line of a stream to files or commands by regex" "pipe,filter,router,partition,awk"` lines, mirroring `trash`.

- [ ] **Step 4: Update `CLAUDE.md`**

Add `Winix.Demux` to the NuGet package IDs list, `demux.json` to the scoop manifests list, and the
`src/Winix.Demux/` + `src/demux/` + `tests/Winix.Demux.Tests/` entries to the project-layout block.

- [ ] **Step 5: Full solution build + test**

Run: `dotnet build Winix.sln && dotnet test Winix.sln`
Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add bucket/demux.json .github/workflows/release.yml .github/workflows/post-publish.yml CLAUDE.md
git commit -m "chore(demux): scoop manifest, release + post-publish wiring, CLAUDE.md"
```

---

## Final verification

- [ ] **Full suite green:** `dotnet test Winix.sln` — 0 failed.
- [ ] **AOT publish works:** `dotnet publish src/demux/demux.csproj -c Release -r linux-x64` (and `win-x64`) — native binary builds.
- [ ] **Manual cmd + pwsh + bash smokes** (per the suite manual-test rule): route to two files + a command, confirm passthrough on stdout, the stderr summary, and exit codes 0 (clean), 1 (kill a child mid-stream), 2 (`--exit-on-child-error` with a child that exits non-zero), 125 (no routes), 126 (unwritable `--to`). Capture to `artifacts/`.

## Notes / known plan risks (resolve during execution)

- **Two-operand flags aren't native to ShellKit** — the custom scan in Task 8 is the load-bearing
  novelty. The scan special-cases only the four route flags; everything else (incl. `--field N` and
  its value) flows to the residual for ShellKit. Edge: a `--` separator — append it and the remainder
  verbatim to the residual (demux has no positionals).
- **Version/parser seam** — build the parser once in `Cli.Run` and thread it into `ArgParser.TryParse`
  (see Task 8/9 notes) to avoid a second parser build with a placeholder version.
- **CommandSink broken-pipe test is timing-sensitive** — see the Task 7 note; pin it if CI flakes.
- **child stdio** — `--exec` children inherit demux's stdout/stderr (not redirected). Documented in the
  README so users know a `tee`-style child echoes onto demux's own stdout.

---

## Adversarial Review Integration — Pass 1 (2026-05-31)

A fresh subagent reviewed this plan against the 15-category taxonomy: **3 blockers, 6 test gaps,
4 defers**. Dispositions below; blocker code is already fixed inline above.

| ID | Bucket | Disposition |
|---|---|---|
| **F1** synchronous multi-child deadlock | Blocker | **Fixed** — Task 7 rewritten: per-`CommandSink` background writer thread + bounded `BlockingCollection`, so one stalled child can't block the router or siblings. ADR **D11**. |
| **F2** `WaitForExit` no timeout / hung child | Blocker | **Fixed** — Task 7 `Close` uses `WaitForExit(10s)` then `Kill(entireProcessTree)` + sentinel exit `-1`. ADR **D11**. |
| **F3** `FileSink` truncates at construction → later open-failure destroys earlier files | Blocker | **Fixed** — Task 9 adds `PreflightFileTargets` (probe all File targets writable, non-truncating, before constructing any sink). Test added (T9-a below). ADR **D12**. |
| **F4** `WriteLine` rewrites LF→CRLF on Windows + appends terminator | Test gap + contract | **Fixed** — all sinks now `Write(line)` + explicit `'\n'`. ADR **D13**. Tests T-NL below. |
| **F5** unbounded single huge line (no-newline blob) can OOM | Explicit defer | Documented: line-oriented = per-line memory bound; `tail -f`-scale many-lines streaming holds, one giant line does not. ADR deferred table + README caveat. Optional 10 MB-line test (T-NL3). |
| **F6** no SIGINT/SIGTERM handling (orphaned children) + no downstream-stdout-closed test | Defer + Test gap | SIGINT child-cleanup **deferred** (ADR). Downstream-closed **test added** (T9-b). |
| **F7** `--all` with a dead sibling — live sibling must still receive the line | Test gap | **Test added** (T-R below). |
| **F8** empty interior field vs out-of-range field (null-vs-empty distinction from Task 5 Step 4) | Test gap | **Test added** (T-R below). |
| **F9** orphaned `--exec` children on SIGINT/SIGKILL/crash | Explicit defer | ADR deferred table. |
| **F10** `--delimiter ""` collides with the whitespace sentinel; multi-char is literal | Explicit defer + small validation | Reject explicit empty `--delimiter` at parse (ArgParser); document literal (non-regex) multi-char. Test T8-a. |
| **F11** shell "command not found" = child `127` → exit 2 under strict, NOT 126 | Test gap | **Integration test added** (T7-a). |
| cat-9 note: `FormatJson` throwing crashes a successful run | (hardening) | **Fixed** — Task 9 computes `ExitCode` before formatting and wraps emission in try/catch. |

### Added tests (fold into the relevant task's test file)

- **T-R (RouterTests):**
  - `All_DeadSibling_StillDeliversToLiveSibling` — two routes both match; sink A `FakeSink(dieAfter:0)`, sink B live; assert B receives the line, A counts undelivered.
  - `Field_EmptyInteriorField_MatchesEmptyRegex` — `--delimiter ","` on `"a,,c"`, field 2, regex `^$` → matches (delivered).
  - `Field_OutOfRange_DoesNotMatchEmptyRegex` — same regex `^$`, field 9 on `"a,b"` → does NOT match (locks the null≠"" distinction).
- **T-NL (newline policy):**
  - `Sink_PreservesLfDoesNotEmitCrlf` (Stdout + File) — write a line, assert output ends in exactly `\n` (assert **without** `Replace("\r\n","\n")` — the masking the review flagged).
  - `Router_EmptyInput_WritesNothingExitZero` — empty stdin → no sink writes, exit 0.
  - `Router_LastLineNoTrailingNewline_StillRouted` — input `"ERROR x"` (no `\n`) routes the line.
  - `(optional) T-NL3 HugeSingleLine_RoutesWithoutError` — one ~10 MB line routes; documents the per-line bound.
- **T9-a (CliTests):** `SecondFileUnopenable_PreservesFirstFileContents_Exits126` — two `--to`; second path under a missing dir; pre-seed the first file; assert exit 126 **and** the first file's original contents intact (fails against pre-F3 code — proves the fix).
- **T9-b (CliTests):** `DownstreamStdoutClosed_MarksPassthroughDead_ExitsOne` — a throwing stdout writer; unmatched line; assert exit 1 and the stdout route reported dead.
- **T7-a (IntegrationTests_CommandSink, Unix, SkippableFact):** `CommandNotFound_Is127_AndExitsTwoUnderStrict` — `--exec '.*' nonexistent-cmd-xyz` with `--exit-on-child-error`; assert child exit 127 and demux exit 2 (confirms it is NOT a 126 setup failure — the D10 boundary).
- **T8-a (ArgParserTests):** `EmptyDelimiter_IsUsageError` — `--delimiter ""` → 125 (the `""` sentinel means whitespace, so an explicit empty value is rejected to avoid the silent collision).

### Summary rendering note
`RoutingSummary.FormatHuman`/`FormatJson` must render `ChildExitCode == -1` as "killed after timeout"
(not literally "exit -1"), and `-1` counts as a non-zero child for the `--exit-on-child-error` → exit 2
rule (though undelivered records from the killed child make exit 1 take precedence).

## Adversarial Review Integration — Pass 2 (confirming, 2026-05-31)

A second fresh subagent confirmed the architecture is sound (not a return-to-brainstorming) but found
**2 blockers the Pass-1 integration itself introduced**, plus 3 test gaps. All fixed in-plan:

| ID | Bucket | Disposition |
|---|---|---|
| **P2-F1** `Close()` relocated the hung-child hang into `_writer.Join()` — the `WaitForExit` timeout sat after the unbounded Join and could never fire | Blocker | **Fixed** — `Close()` now `Join(timeout)`; on overrun `Kill(entireProcessTree)` to unblock the stuck write, *then* the unconditional Join, then sentinel `-1`. Both timeout points (writer-drain and post-EOF wait) bounded. |
| **P2-F2** in-flight line that fails mid-write counted as neither delivered nor undelivered → exit 0 on real data loss | Blocker | **Fixed** — `DrainQueue` catch now `Interlocked.Increment(ref _undelivered)` for the failing line before `break`. Regression test `MidStreamDeath_ConservesLineCount` (delivered+undelivered == written). |
| **P2-F3** CommandSink delivery test still masked CRLF via `.Replace("\r\n","\n")` | Test gap | **Fixed** — assert exact `\n` + `DoesNotContain("\r")` (cat is byte-faithful, so this verifies demux's D13 policy). |
| **P2-F4** no test for the hung-child timeout/kill/`-1` path; 10s real wait is CI-hostile | Test gap | **Fixed** — `_exitTimeout` is now an injectable ctor seam (default 10s); `Close_HungChild_KilledAfterTimeout...` drives it at 300 ms. |
| **P2-F5** Windows `cmd /c` CommandSink coverage was prose-deferred, not a tracked step | Test gap | **Fixed** — promoted to explicit Task 7 Step 4/5 (required), and gated the Windows manual smokes on exercising an `--exec` child, not just file routes. |

**Review status: COMPLETE.** Two passes run (the skill's maximum). The architecture converged at pass 1;
pass 2 found localized integration bugs in pass-1's own fixes, now corrected with regression tests that
verify them at implementation time (TDD closes the loop — no third adversarial pass per the stop
condition). Proceed to execution.
