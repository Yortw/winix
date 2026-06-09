# Winix `agents` User-Scope Pointer — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `winix agents` write the discoverability pointer into user/global agent homes (`~/.claude/CLAUDE.md`, `~/.codex/AGENTS.md`) by default, with project files as an explicit `--project` opt-in carrying conditional (non-asserting) wording.

**Architecture:** Add a render-mode split (assert vs conditional) to the existing block renderer, a known-agent-home table + user-target resolver alongside today's project-target resolver, a home-resolution seam (`ResolveHome`, env-overridable for tests/smokes), and scope dispatch + two new flags in `Cli.cs`. All existing orchestration (dry-run, atomic write, drift, JSON, `remove`) is reused unchanged.

**Tech Stack:** C# / .NET 10, xUnit, `Yort.ShellKit.CommandLineParser`, AOT-compatible (no reflection).

**Design doc:** `docs/superpowers/specs/2026-06-09-winix-agents-user-scope-design.md`

**Branch:** `feature/agents-user-scope` (already created off `release/v0.4.0`).

---

## File Structure

- **Modify** `src/Winix.Winix/AgentsManager.cs` — render mode, home table, `ResolveHome`/`ResolveUserTargets`, scope-aware Run* methods, force-create dir.
- **Modify** `src/Winix.Winix/Cli.cs:74-140` — `--project` + `--codex` flags, scope parse, validation, dispatch, examples.
- **Modify** `tests/Winix.Winix.Tests/AgentsManagerTests.cs` — new render-mode, resolver, force-create, empty-home, project-parity tests; add a fake `IAgentsFileSystem` that implements the new seam members.
- **Modify** `src/winix/README.md` — `agents` section, examples, exit codes, migration note.
- **Modify** `src/winix/winix.1.md` → regenerate `src/winix/man/man1/winix.1`.
- **Modify** `docs/ai/winix.md`, `llms.txt` — agents guidance.
- **Regenerate** `tests/Winix.Contract.Tests/snapshots/winix.describe.json` (new flags appear in `--describe`).
- **Modify** `artifacts/round-stop-2026-05-09/winix/run-smokes.sh` — retarget project cases to `--project`, add user-scope (env-overridden), `--codex`, empty-home cases.

**Reference — current rendered block body (user/assert wording, LF), the baseline for Task 1:**
```
<!-- winix:start v={version} — managed by `winix agents init`; edits between markers are overwritten -->
## Winix CLI tools (available on this machine)

Prefer a Winix tool only when it's genuinely the better choice for the task — not by
default. If you can't say why it beats the platform default (`find`, `time`, `tree`,
`date`, PowerShell, …), use the default.

- **What's installed, flags, JSON shapes:** `winix list` and `<tool> --describe`
  (structured JSON — authoritative for this machine).
- **Full guidance (when to prefer each tool, what it replaces):**
  https://github.com/Yortw/winix/blob/{urlRef}/AGENTS.md
- Conventions: every tool has `--describe` + `--json`; exit 0 = success, non-zero on
  failure (usage/runtime codes vary by tool — see `--describe`); summaries go to stderr
  so stdout stays pipe-clean; `NO_COLOR` respected.
<!-- winix:end -->
```

**Project/conditional wording — only the header line and lead paragraph differ; bullets identical except "this machine" → "the machine running them":**
```
## Winix CLI tools (if available in your environment)

If Winix tools are installed in your environment, prefer one only when it's genuinely the
better choice for the task — not by default. If you can't say why it beats the platform
default (`find`, `time`, `tree`, `date`, PowerShell, …), use the default. If Winix is not
installed, ignore this section.
```

---

## Task 1: Render-mode split (assert vs conditional)

**Files:**
- Modify: `src/Winix.Winix/AgentsManager.cs` (`RenderBlock`, add `RenderMode` enum)
- Test: `tests/Winix.Winix.Tests/AgentsManagerTests.cs`

- [ ] **Step 1: Write failing tests for both render modes**

Add to `AgentsManagerTests.cs` (in the RenderBlock region):

```csharp
[Fact]
public void RenderBlock_UserScope_AssertsAvailabilityAndKeepsRestraint()
{
    string block = AgentsManager.RenderBlock("0.4.0", AgentsManager.RenderMode.UserScope);

    Assert.Contains("## Winix CLI tools (available on this machine)", block, StringComparison.Ordinal);
    Assert.Contains("Prefer a Winix tool only when it's genuinely the better choice", block, StringComparison.Ordinal);
    Assert.DoesNotContain("If Winix is not", block, StringComparison.Ordinal); // no conditional escape hatch in user mode
    Assert.Contains("not by", block, StringComparison.Ordinal);
    Assert.Contains("use the default", block, StringComparison.Ordinal);
}

[Fact]
public void RenderBlock_ProjectScope_UsesConditionalWordingNoInstallAssertion()
{
    string block = AgentsManager.RenderBlock("0.4.0", AgentsManager.RenderMode.ProjectScope);

    Assert.Contains("## Winix CLI tools (if available in your environment)", block, StringComparison.Ordinal);
    Assert.Contains("If Winix tools are installed in your environment", block, StringComparison.Ordinal);
    Assert.Contains("If Winix is not", block, StringComparison.Ordinal); // explicit ignore-if-absent escape hatch
    Assert.DoesNotContain("(available on this machine)", block, StringComparison.Ordinal); // no machine-local assertion
    // Shared invariants hold in both modes:
    Assert.Contains("https://github.com/Yortw/winix/blob/v0.4.0/AGENTS.md", block, StringComparison.Ordinal);
    Assert.Contains("`winix list`", block, StringComparison.Ordinal);
}

[Fact]
public void RenderBlock_DefaultModeIsUserScope()
{
    Assert.Equal(
        AgentsManager.RenderBlock("0.4.0", AgentsManager.RenderMode.UserScope),
        AgentsManager.RenderBlock("0.4.0"));
}
```

- [ ] **Step 2: Run tests, verify they fail to compile**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~RenderBlock"`
Expected: BUILD FAILURE — `RenderMode` does not exist, `RenderBlock` has no 2-arg overload.

- [ ] **Step 3: Implement `RenderMode` and the mode-aware renderer**

In `AgentsManager.cs`, add the enum near the top of the class:

```csharp
/// <summary>Selects the claim strength of the rendered block: <see cref="UserScope"/> asserts the
/// tools are present (written into a per-user agent home); <see cref="ProjectScope"/> uses
/// conditional wording (written into committed files shared with machines that may lack Winix).</summary>
public enum RenderMode { UserScope, ProjectScope }
```

Replace `RenderBlock(string version)` with:

```csharp
internal static string RenderBlock(string version, RenderMode mode = RenderMode.UserScope)
{
    string urlRef = UrlRef(version);
    string header = mode == RenderMode.ProjectScope
        ? "## Winix CLI tools (if available in your environment)"
        : "## Winix CLI tools (available on this machine)";

    string[] lead = mode == RenderMode.ProjectScope
        ? new[]
        {
            "If Winix tools are installed in your environment, prefer one only when it's genuinely the",
            "better choice for the task — not by default. If you can't say why it beats the platform",
            "default (`find`, `time`, `tree`, `date`, PowerShell, …), use the default. If Winix is not",
            "installed, ignore this section.",
        }
        : new[]
        {
            "Prefer a Winix tool only when it's genuinely the better choice for the task — not by",
            "default. If you can't say why it beats the platform default (`find`, `time`, `tree`,",
            "`date`, PowerShell, …), use the default.",
        };

    string authority = mode == RenderMode.ProjectScope
        ? "  (structured JSON — authoritative for the machine running them)."
        : "  (structured JSON — authoritative for this machine).";

    var lines = new List<string>
    {
        $"<!-- winix:start v={version} — managed by `winix agents init`; edits between markers are overwritten -->",
        header,
        "",
    };
    lines.AddRange(lead);
    lines.Add("");
    lines.Add("- **What's installed, flags, JSON shapes:** `winix list` and `<tool> --describe`");
    lines.Add(authority);
    lines.Add("- **Full guidance (when to prefer each tool, what it replaces):**");
    lines.Add($"  https://github.com/Yortw/winix/blob/{urlRef}/AGENTS.md");
    lines.Add("- Conventions: every tool has `--describe` + `--json`; exit 0 = success, non-zero on");
    lines.Add("  failure (usage/runtime codes vary by tool — see `--describe`); summaries go to stderr");
    lines.Add("  so stdout stays pipe-clean; `NO_COLOR` respected.");
    lines.Add("<!-- winix:end -->");
    return string.Join("\n", lines);
}
```

- [ ] **Step 4: Run RenderBlock tests, verify pass**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~RenderBlock"`
Expected: PASS (including the pre-existing `RenderBlock_StableVersion...` and `RenderBlock_PreReleaseVersion...`, which exercise the default = user mode).

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Winix/AgentsManager.cs tests/Winix.Winix.Tests/AgentsManagerTests.cs
git commit -m "feat(agents): render-mode split — assert (user) vs conditional (project) wording"
```

---

## Task 2: Thread render mode through `MergeBlock`

**Files:**
- Modify: `src/Winix.Winix/AgentsManager.cs` (`MergeBlock`)
- Test: `tests/Winix.Winix.Tests/AgentsManagerTests.cs`

- [ ] **Step 1: Write failing test for project-mode merge**

```csharp
[Fact]
public void MergeBlock_ProjectMode_InsertsConditionalBlock()
{
    string merged = AgentsManager.MergeBlock("# Repo\n", "0.4.0", AgentsManager.RenderMode.ProjectScope);

    Assert.Contains("(if available in your environment)", merged, StringComparison.Ordinal);
    Assert.StartsWith("# Repo", merged, StringComparison.Ordinal); // surrounding text preserved
}
```

- [ ] **Step 2: Run, verify fail**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~MergeBlock_ProjectMode"`
Expected: BUILD FAILURE — `MergeBlock` has no 3-arg overload.

- [ ] **Step 3: Add the mode parameter to `MergeBlock`**

Change the signature and the one `RenderBlock` call inside it:

```csharp
internal static string MergeBlock(string content, string version, RenderMode mode = RenderMode.UserScope)
{
    string eol = DetectEol(content);
    string block = NormalizeEol(RenderBlock(version, mode), eol);
    // ... rest unchanged ...
```

- [ ] **Step 4: Run, verify pass**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~MergeBlock"`
Expected: PASS (existing MergeBlock tests still green — they use the default user mode).

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Winix/AgentsManager.cs tests/Winix.Winix.Tests/AgentsManagerTests.cs
git commit -m "feat(agents): thread render mode through MergeBlock"
```

---

## Task 3: Scope + force-codex on `AgentsOptions`; home-resolution seam

**Files:**
- Modify: `src/Winix.Winix/AgentsManager.cs` (`AgentsOptions`, `IAgentsFileSystem`, `DefaultAgentsFileSystem`)
- Test: `tests/Winix.Winix.Tests/AgentsManagerTests.cs`

- [ ] **Step 1: Extend the test fake to implement the new seam members**

If `AgentsManagerTests.cs` already defines an in-memory `IAgentsFileSystem` fake, add the two new members; otherwise add this fake near the top of the test class:

```csharp
private sealed class FakeFs : IAgentsFileSystem
{
    public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Dirs { get; } = new(StringComparer.Ordinal);
    public string Home { get; set; } = "/home/u";
    public List<string> Created { get; } = new();
    // (Review F4) Fault-injection knob: when set, WriteAllText throws for paths containing this
    // substring, so the IOException-surfacing contract in Run* is reachable by a deterministic test.
    public string? ThrowOnWritePath { get; set; }

    public bool FileExists(string path) => Files.ContainsKey(path);
    public bool DirectoryExists(string path) => Dirs.Contains(path);
    public string ReadAllText(string path) => Files[path];
    public void WriteAllText(string path, string content)
    {
        if (ThrowOnWritePath != null && path.Contains(ThrowOnWritePath, StringComparison.Ordinal))
        {
            throw new IOException("injected write failure");
        }
        Files[path] = content;
    }
    public string ResolveHome() => Home;
    public void CreateDirectory(string path) { Dirs.Add(path); Created.Add(path); }
}
```

- [ ] **Step 2: Write failing test asserting the new option shape and seam exist**

```csharp
[Fact]
public void AgentsOptions_CarriesScopeAndForceCodex()
{
    var opts = new AgentsManager.AgentsOptions(
        Verb: "init", Scope: AgentsManager.AgentsScope.User, BaseDir: ".",
        ForceClaude: false, ForceCodex: true, DryRun: false, Json: false, Version: "0.4.0");

    Assert.Equal(AgentsManager.AgentsScope.User, opts.Scope);
    Assert.True(opts.ForceCodex);
}
```

- [ ] **Step 3: Run, verify fail**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~AgentsOptions_Carries"`
Expected: BUILD FAILURE — `AgentsScope` / `Scope` / `ForceCodex` undefined; `IAgentsFileSystem` lacks `ResolveHome`/`CreateDirectory`.

- [ ] **Step 4: Implement scope enum, option fields, and seam members**

Add the enum and extend the record in `AgentsManager.cs`:

```csharp
/// <summary>Target location for the managed block: <see cref="User"/> = per-user agent homes
/// (default); <see cref="Project"/> = committed files in a repo directory.</summary>
public enum AgentsScope { User, Project }

public sealed record AgentsOptions(
    string? Verb,
    AgentsScope Scope,
    string BaseDir,
    bool ForceClaude,
    bool ForceCodex,
    bool DryRun,
    bool Json,
    string Version);
```

Add to `IAgentsFileSystem`:

```csharp
/// <summary>Returns the current user's home directory (parent of agent-config dirs like
/// <c>.claude</c>). Production honours the <c>WINIX_AGENTS_HOME</c> override before falling back
/// to the OS user-profile path, so smoke tests can redirect writes to a scratch dir.</summary>
string ResolveHome();

/// <summary>Creates <paramref name="path"/> and any missing parents (idempotent). Used to
/// force-create an agent home (<c>--claude</c>/<c>--codex</c>) whose dir does not yet exist.</summary>
void CreateDirectory(string path);
```

Implement in `DefaultAgentsFileSystem`:

```csharp
public string ResolveHome()
{
    // WINIX_AGENTS_HOME lets smokes/integration tests redirect user-scope writes to a scratch
    // dir instead of the developer's real ~/.claude — never clobber a real agent config in CI.
    // Contract (test/smoke use only): must be an ABSOLUTE path. A relative value is normalised
    // against CWD via GetFullPath so behaviour is deterministic rather than silently CWD-relative
    // at each Path.Combine. It need NOT exist — a non-existent override resolves to the empty-home
    // path (no homes found) for a non-force init, which is the intended "nothing set up" outcome.
    string? overrideHome = Environment.GetEnvironmentVariable("WINIX_AGENTS_HOME");
    return !string.IsNullOrEmpty(overrideHome)
        ? Path.GetFullPath(overrideHome)
        : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}

public void CreateDirectory(string path) => Directory.CreateDirectory(path);
```

> **(Review F1)** Document the `WINIX_AGENTS_HOME` contract in the `IAgentsFileSystem.ResolveHome` XML-doc and the README: absolute path, test/smoke isolation only, may be non-existent. The file-collision case (override points at a regular file, then `--codex` force-creates `<file>/.codex`) already maps to a clean `InternalError` via the existing `IOException` catch — no crash, just an opaque type name; acceptable for a test-only knob.

Also make `WriteAllText` ensure the target dir exists (force-created homes have no dir yet). Add at the top of `DefaultAgentsFileSystem.WriteAllText`, before computing `temp`:

```csharp
string? parent = Path.GetDirectoryName(path);
if (!string.IsNullOrEmpty(parent)) { Directory.CreateDirectory(parent); }
```

- [ ] **Step 5: Run, verify pass + full project builds**

Run: `dotnet build src/Winix.Winix/Winix.Winix.csproj`
Expected: BUILD SUCCEEDS.
Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~AgentsOptions_Carries"`
Expected: PASS. (Other tests referencing `new AgentsOptions(...)` will now fail to compile — that is fixed in Task 4 where the Run* call sites are updated; if the test project does not yet compile, proceed to Task 4 before re-running the full suite.)

- [ ] **Step 6: Commit**

```bash
git add src/Winix.Winix/AgentsManager.cs tests/Winix.Winix.Tests/AgentsManagerTests.cs
git commit -m "feat(agents): add AgentsScope + ForceCodex + ResolveHome/CreateDirectory seam"
```

---

## Task 4: Known-home table + `ResolveUserTargets`

**Files:**
- Modify: `src/Winix.Winix/AgentsManager.cs`
- Test: `tests/Winix.Winix.Tests/AgentsManagerTests.cs`

- [ ] **Step 1: Write failing tests for user-target resolution**

```csharp
private static AgentsManager.AgentsOptions UserOpts(bool forceClaude = false, bool forceCodex = false) =>
    new("init", AgentsManager.AgentsScope.User, ".", forceClaude, forceCodex, false, false, "0.4.0");

[Fact]
public void ResolveUserTargets_OnlyExistingHomes()
{
    var fs = new FakeFs { Home = "/home/u" };
    fs.Dirs.Add("/home/u/.claude"); // codex dir absent

    var targets = AgentsManager.ResolveUserTargets(UserOpts(), fs);

    Assert.Equal(new[] { "/home/u/.claude/CLAUDE.md" }, targets);
}

[Fact]
public void ResolveUserTargets_ForceCodexIncludesAbsentHome()
{
    var fs = new FakeFs { Home = "/home/u" };
    fs.Dirs.Add("/home/u/.claude");

    var targets = AgentsManager.ResolveUserTargets(UserOpts(forceCodex: true), fs);

    Assert.Contains("/home/u/.claude/CLAUDE.md", targets);
    Assert.Contains("/home/u/.codex/AGENTS.md", targets);
}

[Fact]
public void ResolveUserTargets_NoHomesNoForce_Empty()
{
    var fs = new FakeFs { Home = "/home/u" };
    Assert.Empty(AgentsManager.ResolveUserTargets(UserOpts(), fs));
}
```

> Path note: assertions use forward-slash paths because `Path.Combine` on the *nix CI leg produces them; on Windows the same test runs with `\`. To stay OS-agnostic, the implementation and tests both build expected paths via `Path.Combine`. Rewrite the literal-string asserts above as `Path.Combine(fs.Home, ".claude", "CLAUDE.md")` etc. at implementation time so the test passes on both legs. (Verify-at-implementation: confirm separator handling on the Windows runner.)

- [ ] **Step 2: Run, verify fail**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~ResolveUserTargets"`
Expected: BUILD FAILURE — `ResolveUserTargets` / home table undefined.

- [ ] **Step 3: Implement the home table and resolver**

```csharp
/// <summary>A known agent-config home: the dir under the user profile and the Markdown file
/// within it that the agent reads as global context.</summary>
internal readonly record struct AgentHome(string Id, string Dir, string File);

/// <summary>The user-scope homes <c>winix agents</c> manages, in write order. Adding a third
/// agent is a single row — no other code changes.</summary>
internal static readonly AgentHome[] KnownHomes =
{
    new AgentHome("claude", ".claude", "CLAUDE.md"),
    new AgentHome("codex", ".codex", "AGENTS.md"),
};

/// <summary>Resolves the user-home files to act on: every known home whose dir exists, plus any
/// home named by a force flag (<c>--claude</c>/<c>--codex</c>) even when its dir is absent.</summary>
internal static List<string> ResolveUserTargets(AgentsOptions opts, IAgentsFileSystem fs)
{
    string home = fs.ResolveHome();
    var targets = new List<string>();
    foreach (AgentHome h in KnownHomes)
    {
        string dir = Path.Combine(home, h.Dir);
        bool forced = (h.Id == "claude" && opts.ForceClaude) || (h.Id == "codex" && opts.ForceCodex);
        if (forced || fs.DirectoryExists(dir))
        {
            targets.Add(Path.Combine(dir, h.File));
        }
    }
    return targets;
}
```

- [ ] **Step 4: Run, verify pass**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~ResolveUserTargets"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Winix/AgentsManager.cs tests/Winix.Winix.Tests/AgentsManagerTests.cs
git commit -m "feat(agents): known-home table + ResolveUserTargets resolver"
```

---

## Task 5: Scope-aware `RunInit` (user default, project opt-in, empty-home error)

**Files:**
- Modify: `src/Winix.Winix/AgentsManager.cs` (`RunInit`, plus a shared target-resolution helper)
- Test: `tests/Winix.Winix.Tests/AgentsManagerTests.cs`

- [ ] **Step 1: Write failing tests for init across scopes**

```csharp
[Fact]
public void RunInit_UserScope_WritesExistingHomesWithAssertWording()
{
    var fs = new FakeFs { Home = "/home/u" };
    fs.Dirs.Add(Path.Combine("/home/u", ".claude"));
    var sw = new StringWriter();

    int code = AgentsManager.RunInit(UserOpts(), fs, sw, sw);

    Assert.Equal(WinixExitCode.Success, code);
    string written = fs.Files[Path.Combine("/home/u", ".claude", "CLAUDE.md")];
    Assert.Contains("(available on this machine)", written, StringComparison.Ordinal);
}

[Fact]
public void RunInit_UserScope_ForceCodex_CreatesDirAndWrites()
{
    var fs = new FakeFs { Home = "/home/u" }; // no homes exist
    var sw = new StringWriter();

    int code = AgentsManager.RunInit(UserOpts(forceCodex: true), fs, sw, sw);

    Assert.Equal(WinixExitCode.Success, code);
    Assert.Contains(Path.Combine("/home/u", ".codex"), fs.Created);
    Assert.True(fs.Files.ContainsKey(Path.Combine("/home/u", ".codex", "AGENTS.md")));
}

[Fact]
public void RunInit_UserScope_NoHomesNoForce_UsageErrorWritesNothing()
{
    var fs = new FakeFs { Home = "/home/u" };
    var sw = new StringWriter();

    int code = AgentsManager.RunInit(UserOpts(), fs, sw, sw);

    Assert.Equal(WinixExitCode.UsageError, code);
    Assert.Empty(fs.Files);
    Assert.Contains("no agent home found", sw.ToString(), StringComparison.Ordinal);
}

[Fact]
public void RunInit_ProjectScope_WritesConditionalWording()
{
    var fs = new FakeFs();
    fs.Dirs.Add("/repo");
    var opts = new AgentsManager.AgentsOptions(
        "init", AgentsManager.AgentsScope.Project, "/repo", false, false, false, false, "0.4.0");
    var sw = new StringWriter();

    int code = AgentsManager.RunInit(opts, fs, sw, sw);

    Assert.Equal(WinixExitCode.Success, code);
    Assert.Contains("(if available in your environment)",
        fs.Files[Path.Combine("/repo", "AGENTS.md")], StringComparison.Ordinal);
}

[Fact] // (Review F4) IOException mid-write surfaces InternalError + names the failing target.
public void RunInit_WriteFailure_InternalErrorNamesTarget()
{
    var fs = new FakeFs { Home = "/home/u", ThrowOnWritePath = ".claude" };
    fs.Dirs.Add(Path.Combine("/home/u", ".claude"));
    var sw = new StringWriter();

    int code = AgentsManager.RunInit(UserOpts(), fs, sw, sw);

    Assert.Equal(WinixExitCode.InternalError, code);
    Assert.Contains(Path.Combine("/home/u", ".claude", "CLAUDE.md"), sw.ToString(), StringComparison.Ordinal);
    // Resource-key history: the surfaced text must not leak a bare framework SR key.
    Assert.DoesNotContain("Arg_", sw.ToString(), StringComparison.Ordinal);
}

[Fact] // (Review F1) A non-existent WINIX_AGENTS_HOME resolves to empty-home, not a crash.
public void RunInit_NonExistentHome_NoForce_EmptyHomeUsageError()
{
    var fs = new FakeFs { Home = "/does/not/exist" }; // no dirs registered
    var sw = new StringWriter();

    int code = AgentsManager.RunInit(UserOpts(), fs, sw, sw);

    Assert.Equal(WinixExitCode.UsageError, code);
    Assert.Contains("no agent home found", sw.ToString(), StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run, verify fail**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~RunInit_UserScope|FullyQualifiedName~RunInit_ProjectScope|FullyQualifiedName~RunInit_WriteFailure|FullyQualifiedName~RunInit_NonExistentHome"`
Expected: FAIL (current `RunInit` ignores scope, has no empty-home path, writes user-mode wording for project).

- [ ] **Step 3: Add a shared target/precondition resolver and rewrite `RunInit`**

Add a helper that both `RunInit`/`RunStatus`/`RunRemove` reuse:

```csharp
/// <summary>Resolves the files an action operates on for the given scope, and the render mode.
/// Returns <see langword="false"/> with <paramref name="errorMessage"/> set when a scope
/// precondition fails (project base dir missing, or user scope with no home and no force flag).</summary>
internal static bool TryResolveTargets(
    AgentsOptions opts, IAgentsFileSystem fs,
    out List<string> targets, out RenderMode mode, out string? errorMessage, out int errorCode)
{
    errorMessage = null;
    errorCode = WinixExitCode.Success;
    if (opts.Scope == AgentsScope.Project)
    {
        mode = RenderMode.ProjectScope;
        if (!fs.DirectoryExists(opts.BaseDir))
        {
            targets = new List<string>();
            errorMessage = $"winix: path '{opts.BaseDir}' is not a directory";
            errorCode = WinixExitCode.UsageError;
            return false;
        }
        targets = ResolveInitTargets(opts, fs);
        return true;
    }

    mode = RenderMode.UserScope;
    targets = ResolveUserTargets(opts, fs);
    if (targets.Count == 0)
    {
        errorMessage = "winix: no agent home found (use --claude or --codex to create one)";
        errorCode = WinixExitCode.UsageError;
        return false;
    }
    return true;
}
```

Rewrite `RunInit` to use it (replacing the current `DirectoryExists(opts.BaseDir)` guard and the hard-coded `ResolveInitTargets`/`MergeBlock(existing, opts.Version)` calls):

```csharp
internal static int RunInit(AgentsOptions opts, IAgentsFileSystem fs, TextWriter stdout, TextWriter stderr)
{
    if (!TryResolveTargets(opts, fs, out List<string> targets, out RenderMode mode, out string? err, out int code))
    {
        stderr.WriteLine(err);
        return code;
    }

    var changed = new List<string>();
    string current = string.Empty;
    try
    {
        foreach (string target in targets)
        {
            current = target;
            string existing = fs.FileExists(target) ? fs.ReadAllText(target) : string.Empty;
            string merged = MergeBlock(existing, opts.Version, mode);
            if (!opts.DryRun)
            {
                // (Review F9) Ensure the target dir exists through the SEAM before writing — this
                // makes force-create (--claude/--codex into an absent home) deterministically unit-
                // testable (assert fs.Created) instead of relying on the production writer's own
                // dir-creation, which the in-memory fake can't observe. Idempotent for existing dirs.
                string? dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir)) { fs.CreateDirectory(dir); }
                fs.WriteAllText(target, merged);
            }
            changed.Add(target);
            if (!opts.Json)
            {
                stderr.WriteLine(opts.DryRun ? $"winix: would write {target}" : $"winix: wrote {target}");
            }
        }
        if (opts.Json) { stdout.WriteLine(FormatActionJson("init", opts.DryRun, changed)); }
        return WinixExitCode.Success;
    }
    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
    {
        stderr.WriteLine($"winix: failed to write agents pointer to {current} ({ex.GetType().Name})");
        return WinixExitCode.InternalError;
    }
}
```

> **(Review F9)** `RunInit` now creates the target dir through the `IAgentsFileSystem.CreateDirectory` seam (above) before `WriteAllText`, so the `RunInit_UserScope_ForceCodex_CreatesDirAndWrites` test's `fs.Created` assertion is meaningful and deterministic — keep it. The production `DefaultAgentsFileSystem.WriteAllText` dir-creation from Task 3 stays as belt-and-braces. Do NOT drop the `fs.Created` assertion.

- [ ] **Step 4: Run, verify pass**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~RunInit"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Winix.Winix/AgentsManager.cs tests/Winix.Winix.Tests/AgentsManagerTests.cs
git commit -m "feat(agents): scope-aware RunInit with user default + empty-home error"
```

---

## Task 6: Scope-aware `RunStatus` and `RunRemove`

**Files:**
- Modify: `src/Winix.Winix/AgentsManager.cs` (`RunStatus`, `RunRemove`)
- Test: `tests/Winix.Winix.Tests/AgentsManagerTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public void RunStatus_UserScope_NoHome_ReportsNotCurrent()
{
    var fs = new FakeFs { Home = "/home/u" };
    var sw = new StringWriter();

    int code = AgentsManager.RunStatus(UserOpts(), fs, sw, sw);

    Assert.Equal(WinixExitCode.ToolFailure, code);
    Assert.Contains("no agent home found", sw.ToString(), StringComparison.Ordinal);
}

[Fact]
public void RunStatus_UserScope_CurrentBlock_Zero()
{
    var fs = new FakeFs { Home = "/home/u" };
    string claudeDir = Path.Combine("/home/u", ".claude");
    fs.Dirs.Add(claudeDir);
    fs.Files[Path.Combine(claudeDir, "CLAUDE.md")] =
        AgentsManager.MergeBlock(string.Empty, "0.4.0", AgentsManager.RenderMode.UserScope);
    var sw = new StringWriter();

    Assert.Equal(WinixExitCode.Success, AgentsManager.RunStatus(UserOpts(), fs, sw, sw));
}

[Fact]
public void RunRemove_UserScope_StripsBlock()
{
    var fs = new FakeFs { Home = "/home/u" };
    string claudeDir = Path.Combine("/home/u", ".claude");
    fs.Dirs.Add(claudeDir);
    string file = Path.Combine(claudeDir, "CLAUDE.md");
    fs.Files[file] = AgentsManager.MergeBlock("# keep me\n", "0.4.0", AgentsManager.RenderMode.UserScope);
    var sw = new StringWriter();

    Assert.Equal(WinixExitCode.Success, AgentsManager.RunRemove(UserOpts(), fs, sw, sw));
    Assert.DoesNotContain("winix:start", fs.Files[file], StringComparison.Ordinal);
    Assert.Contains("# keep me", fs.Files[file], StringComparison.Ordinal);
}

[Fact] // (Review F8) Pin the no-home --json shape so the no-home/absent-block ambiguity is a
       // conscious, documented choice rather than an accident. allCurrent must be false.
public void RunStatus_UserScope_NoHome_Json_ShapePinned()
{
    var fs = new FakeFs { Home = "/home/u" };
    var sw = new StringWriter();
    var opts = new AgentsManager.AgentsOptions(
        "status", AgentsManager.AgentsScope.User, ".", false, false, false, Json: true, "0.4.0");

    int code = AgentsManager.RunStatus(opts, fs, sw, sw);

    Assert.Equal(WinixExitCode.ToolFailure, code);
    Assert.Contains("\"current\":false", sw.ToString(), StringComparison.Ordinal);
    Assert.Contains("\"files\":[]", sw.ToString(), StringComparison.Ordinal);
}

[Fact] // (Review F2) KNOWN LIMITATION: markers encode version only, not render mode. A current-
       // version block whose WORDING is the wrong mode reports "current" — status can't detect
       // wording drift. This test documents that behaviour so it's a conscious accepted limitation.
public void RunStatus_UserScope_ProjectWordedBlockAtCurrentVersion_ReportsCurrent()
{
    var fs = new FakeFs { Home = "/home/u" };
    string claudeDir = Path.Combine("/home/u", ".claude");
    fs.Dirs.Add(claudeDir);
    // A project-mode block (conditional wording) sitting in a USER home file at the current version.
    fs.Files[Path.Combine(claudeDir, "CLAUDE.md")] =
        AgentsManager.MergeBlock(string.Empty, "0.4.0", AgentsManager.RenderMode.ProjectScope);
    var sw = new StringWriter();

    // Reported current despite wrong-mode wording — markers carry no mode token. Accepted limitation.
    Assert.Equal(WinixExitCode.Success, AgentsManager.RunStatus(UserOpts(), fs, sw, sw));
}

[Fact] // (Review F7) The mode-agnostic marker parser still finds + removes a PROJECT-mode block.
       // Cross-mode belt-and-braces over the existing parser tests (FindBlockVersion_StartWithoutEnd_
       // ReturnsNull, RemoveBlock duplicate-strip) which already pin malformed-marker handling.
public void RemoveBlock_ProjectModeBlock_StrippedByModeAgnosticParser()
{
    string file = "# Repo\n\n" + AgentsManager.RenderBlock("0.4.0", AgentsManager.RenderMode.ProjectScope) + "\n";
    Assert.Equal("0.4.0", AgentsManager.FindBlockVersion(file));
    string after = AgentsManager.RemoveBlock(file);
    Assert.DoesNotContain("winix:start", after, StringComparison.Ordinal);
    Assert.Contains("# Repo", after, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run, verify fail**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~RunStatus_UserScope|FullyQualifiedName~RunRemove_UserScope|FullyQualifiedName~RemoveBlock_ProjectMode"`
Expected: FAIL (the new scope-aware Run* methods don't exist yet; `RemoveBlock_ProjectMode` compiles only after Task 1's `RenderMode`).

- [ ] **Step 3: Rewrite `RunStatus` to resolve targets by scope**

Replace the `DirectoryExists(opts.BaseDir)` guard + `ResolveInitTargets` loop with scope resolution. For the no-home user case, `status` returns `ToolFailure` (not `UsageError`) so the `status || init` idiom triggers init:

```csharp
internal static int RunStatus(AgentsOptions opts, IAgentsFileSystem fs, TextWriter stdout, TextWriter stderr)
{
    List<string> targets;
    if (opts.Scope == AgentsScope.Project)
    {
        if (!fs.DirectoryExists(opts.BaseDir))
        {
            stderr.WriteLine($"winix: path '{opts.BaseDir}' is not a directory");
            return WinixExitCode.UsageError;
        }
        targets = ResolveInitTargets(opts, fs);
    }
    else
    {
        targets = ResolveUserTargets(opts, fs);
        if (targets.Count == 0)
        {
            if (opts.Json) { stdout.WriteLine(FormatStatusJson(new(), allCurrent: false)); }
            else { stderr.WriteLine("winix: no agent home found"); }
            return WinixExitCode.ToolFailure;
        }
    }

    var results = new List<(string Path, string State, string? Version)>();
    bool allCurrent = true;
    string current = string.Empty;
    try
    {
        foreach (string target in targets)
        {
            current = target;
            string? blockVer = fs.FileExists(target) ? FindBlockVersion(fs.ReadAllText(target)) : null;
            string state;
            if (blockVer == null) { state = "absent"; allCurrent = false; }
            else if (string.Equals(blockVer, opts.Version, StringComparison.Ordinal)) { state = "current"; }
            else { state = "stale"; allCurrent = false; }
            results.Add((target, state, blockVer));
        }
    }
    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
    {
        stderr.WriteLine($"winix: failed to read agents pointer from {current} ({ex.GetType().Name})");
        return WinixExitCode.InternalError;
    }

    if (opts.Json) { stdout.WriteLine(FormatStatusJson(results, allCurrent)); }
    else
    {
        foreach ((string path, string state, string? version) in results)
        {
            string suffix = version != null ? $" (v{version})" : string.Empty;
            stderr.WriteLine($"winix: {path}: {state}{suffix}");
        }
    }
    return allCurrent ? WinixExitCode.Success : WinixExitCode.ToolFailure;
}
```

- [ ] **Step 4: Rewrite `RunRemove` candidate resolution by scope**

Replace the hard-coded `candidates` array. Project scope keeps AGENTS.md + CLAUDE.md; user scope uses every known home file (regardless of force flags — removing is safe and idempotent):

```csharp
internal static int RunRemove(AgentsOptions opts, IAgentsFileSystem fs, TextWriter stdout, TextWriter stderr)
{
    string[] candidates;
    if (opts.Scope == AgentsScope.Project)
    {
        if (!fs.DirectoryExists(opts.BaseDir))
        {
            stderr.WriteLine($"winix: path '{opts.BaseDir}' is not a directory");
            return WinixExitCode.UsageError;
        }
        candidates = new[]
        {
            Path.Combine(opts.BaseDir, "AGENTS.md"),
            Path.Combine(opts.BaseDir, "CLAUDE.md"),
        };
    }
    else
    {
        string home = fs.ResolveHome();
        var list = new List<string>();
        foreach (AgentHome h in KnownHomes) { list.Add(Path.Combine(home, h.Dir, h.File)); }
        candidates = list.ToArray();
    }

    var changed = new List<string>();
    string current = string.Empty;
    try
    {
        foreach (string target in candidates)
        {
            current = target;
            if (!fs.FileExists(target)) { continue; }
            string existing = fs.ReadAllText(target);
            if (FindBlockVersion(existing) == null) { continue; }
            if (!opts.DryRun) { fs.WriteAllText(target, RemoveBlock(existing)); }
            changed.Add(target);
            if (!opts.Json)
            {
                stderr.WriteLine(opts.DryRun ? $"winix: would update {target}" : $"winix: removed block from {target}");
            }
        }
        if (opts.Json) { stdout.WriteLine(FormatActionJson("remove", opts.DryRun, changed)); }
        return WinixExitCode.Success;
    }
    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
    {
        stderr.WriteLine($"winix: failed to update agents pointer to {current} ({ex.GetType().Name})");
        return WinixExitCode.InternalError;
    }
}
```

- [ ] **Step 5: Run, verify pass + full AgentsManager suite green**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~AgentsManager"`
Expected: PASS (all old project-scope tests must be updated to pass `AgentsScope.Project` + the new arity — do that as part of this step; see Task 7 caller-audit note).

- [ ] **Step 6: Commit**

```bash
git add src/Winix.Winix/AgentsManager.cs tests/Winix.Winix.Tests/AgentsManagerTests.cs
git commit -m "feat(agents): scope-aware RunStatus + RunRemove"
```

---

## Task 7: Update every existing `AgentsOptions` caller (test caller-audit)

**Files:**
- Modify: `tests/Winix.Winix.Tests/AgentsManagerTests.cs` (all pre-existing `new AgentsOptions(...)` sites)

- [ ] **Step 1: Find all constructor call sites**

Run: `grep -rn "new AgentsManager.AgentsOptions\|new AgentsOptions" tests/ src/`
Expected: a list of sites in the test file and `Cli.cs` (Cli.cs is handled in Task 8).

- [ ] **Step 2: Update each test site to the new 8-arg shape**

For every pre-existing `new AgentsOptions(Verb, BaseDir, ForceClaude, DryRun, Json, Version)` insert `Scope` (positional 2) and `ForceCodex` (after `ForceClaude`). Project-behaviour tests use `AgentsScope.Project`; the directory-missing and `--claude`-parity tests stay project scope so their assertions hold unchanged.

- [ ] **Step 3: Run full test project, verify compile + pass**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj`
Expected: PASS, 0 failures.

- [ ] **Step 4: Commit**

```bash
git add tests/Winix.Winix.Tests/AgentsManagerTests.cs
git commit -m "test(agents): migrate AgentsOptions callers to scope-aware arity"
```

---

## Task 8: CLI wiring — `--project`, `--codex`, scope dispatch, validation

**Files:**
- Modify: `src/Winix.Winix/Cli.cs:74-140`
- Test: `tests/Winix.Winix.Tests/` (CLI-level test — match the existing Cli test file pattern; if none covers agents flag-wiring, add `CliAgentsScopeTests.cs`)

- [ ] **Step 1: Write failing CLI wiring tests**

Create `tests/Winix.Winix.Tests/CliAgentsScopeTests.cs` (adapt namespace/host to the existing Cli test harness — check how other `Cli.RunAsync` tests invoke it and whether a fake fs can be injected; if `Cli` does not accept an `IAgentsFileSystem`, these assert on exit code + stderr text only):

```csharp
#nullable enable
using System.IO;
using System.Threading.Tasks;
using Winix.Winix;
using Xunit;

namespace Winix.Winix.Tests;

public sealed class CliAgentsScopeTests
{
    [Fact]
    public async Task PathWithoutProject_IsUsageError()
    {
        var sw = new StringWriter();
        int code = await Cli.RunAsync(new[] { "agents", "status", "--path", "." }, sw, sw);
        Assert.Equal(WinixExitCode.UsageError, code);
        Assert.Contains("--path", sw.ToString());
    }

    [Fact]
    public async Task ProjectWithCodex_IsUsageError()
    {
        var sw = new StringWriter();
        int code = await Cli.RunAsync(new[] { "agents", "init", "--project", "--codex" }, sw, sw);
        Assert.Equal(WinixExitCode.UsageError, code);
        Assert.Contains("--codex", sw.ToString());
    }
}
```

> Verify-at-implementation: confirm the exact `Cli.RunAsync` signature (it takes `stdout, stderr`, possibly a `PlatformId?` and `manifestLoader`) from `Cli.cs:55-61` and match it. These two validation paths return before any manifest fetch, so a null manifest loader is fine.

- [ ] **Step 2: Run, verify fail**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~CliAgentsScope"`
Expected: FAIL (no validation yet; `--codex`/`--project` not declared so parser errors with a different message, or `--path` is silently accepted).

- [ ] **Step 3: Declare the new flags and update descriptions**

In the parser builder (`Cli.cs:74-75`), update `--claude` and `--path`, and add `--project`/`--codex`:

```csharp
.Flag("--project", "Write into committed project files (AGENTS.md/CLAUDE.md) instead of user/global agent config (agents only)")
.Flag("--claude", "Force the Claude home/file even when absent: user scope → ~/.claude/CLAUDE.md; --project → include CLAUDE.md (agents only)")
.Flag("--codex", "Force the Codex user home (~/.codex/AGENTS.md) even when absent (agents user scope only)")
.Option("--path", null, "DIR", "Project directory for --project (agents only; default: current directory)")
```

- [ ] **Step 4: Add scope parse + validation + dispatch in the `command == "agents"` block**

Replace the body of `if (command == "agents") { ... }` (`Cli.cs:122-141`):

```csharp
if (command == "agents")
{
    bool project = result.Has("--project");

    // --path is only meaningful for a project directory; reject it in user scope so a
    // misplaced --path can't silently no-op against the wrong target.
    if (result.Has("--path") && !project)
    {
        return result.WriteError("--path is only valid with --project (user scope writes to your agent home)", stderr);
    }
    // --codex names a user home; it has no meaning when writing committed project files.
    if (result.Has("--codex") && project)
    {
        return result.WriteError("--codex is a user-scope flag and cannot be combined with --project", stderr);
    }

    string? verb = result.Positionals.Length > 1 ? result.Positionals[1] : null;
    string baseDir = result.Has("--path")
        ? result.GetString("--path")!
        : Directory.GetCurrentDirectory();

    var agentsOptions = new AgentsManager.AgentsOptions(
        Verb: verb,
        Scope: project ? AgentsManager.AgentsScope.Project : AgentsManager.AgentsScope.User,
        BaseDir: baseDir,
        ForceClaude: result.Has("--claude"),
        ForceCodex: result.Has("--codex"),
        DryRun: result.Has("--dry-run"),
        Json: result.Has("--json"),
        Version: version);

    return AgentsManager.Run(agentsOptions, stdout, stderr);
}
```

- [ ] **Step 5: Update the agents `--help` examples and exit-code text**

Replace the three agents `.Example(...)` lines (`Cli.cs:92-94`) and confirm the exit-code description still reads true:

```csharp
.Example("winix agents init", "Write the Winix pointer into your user agent config (~/.claude/CLAUDE.md, ~/.codex/AGENTS.md)")
.Example("winix agents init --project", "Write a conditional pointer into this repo's AGENTS.md/CLAUDE.md (for teams standardized on Winix)")
.Example("winix agents status", "Report whether your user agent config carries a current pointer (exit 1 if not)")
.Example("winix agents remove", "Remove the Winix pointer block from your user agent config")
```

- [ ] **Step 6: Run, verify pass + full solution builds**

Run: `dotnet test tests/Winix.Winix.Tests/Winix.Winix.Tests.csproj --filter "FullyQualifiedName~CliAgentsScope"`
Expected: PASS.
Run: `dotnet build Winix.sln`
Expected: BUILD SUCCEEDS, 0 warnings (warnings-as-errors).

- [ ] **Step 7: Commit**

```bash
git add src/Winix.Winix/Cli.cs tests/Winix.Winix.Tests/CliAgentsScopeTests.cs
git commit -m "feat(agents): CLI --project/--codex scope dispatch + validation"
```

---

## Task 9: Smoke fixture — retarget project cases, add user-scope/codex/empty-home

**Files:**
- Modify: `artifacts/round-stop-2026-05-09/winix/run-smokes.sh:116-138`

- [ ] **Step 0 (Review F5 — BLOCKER): Determine the `smoke` helper's expected-exit-code mechanism BEFORE writing any case**

Read the `smoke` (and `cp`) helper definitions at the top of `artifacts/round-stop-2026-05-09/winix/run-smokes.sh`. Establish exactly how a case declares an expected NON-zero exit (does it parse `-> 125` from the label, take an expected-code argument, or always expect 0?). State the mechanism in a comment in the A-section. **This is load-bearing:** if the helper ignores exit codes, every negative-path case (no-verb, drift, empty-home, validation errors) is false-green and the whole negative-path smoke surface is worthless — the exact "verification that constrains nothing" failure mode. Then add a **known-nonzero control** as the first case and confirm the harness reports it RED when the command unexpectedly succeeds:

```bash
# CONTROL: this MUST be reported as a failure by the harness if exit-code checking works.
# (Per "run a known-nonzero control first" — proves the negative-path smokes below have teeth.)
smoke A00 "CONTROL agents bad verb -> nonzero" -- "$WINIX_EXE" agents frobnicate
```

Do not proceed to Step 1 until the expected-nonzero mechanism is confirmed and the control demonstrably fails-red on a forced success.

- [ ] **Step 1: Rewrite the A-section so default cases are user scope (env-isolated) and project cases use `--project`**

Replace lines 116-138 with (preserving the `smoke`/`cp` helper conventions already in the file):

```bash
# ----- A: agents subcommand (writes files; isolated temp dirs + WINIX_AGENTS_HOME) -----

AGHOME="$RES/agents-home"        # fake user home for user-scope cases
AGREPO="$RES/agents-repo"        # fake repo for --project cases
AGREPO2="$RES/agents-repo-claude"
rm -rf "$AGHOME" "$AGREPO" "$AGREPO2"
mkdir -p "$AGHOME/.claude" "$AGREPO" "$AGREPO2"   # .claude exists so default user init has a home

# User scope (default) — redirected to the fake home so the real ~/.claude is never touched.
smoke A01 "agents no verb -> usage 125"            -- env WINIX_AGENTS_HOME="$AGHOME" "$WINIX_EXE" agents
smoke A02 "agents init (user) writes ~/.claude -> 0" -- env WINIX_AGENTS_HOME="$AGHOME" "$WINIX_EXE" agents init
smoke A03 "agents status (user) current -> 0"      -- env WINIX_AGENTS_HOME="$AGHOME" "$WINIX_EXE" agents status
smoke A04 "agents status --json current -> 0"      -- env WINIX_AGENTS_HOME="$AGHOME" "$WINIX_EXE" agents status --json
smoke A05 "agents init idempotent re-run -> 0"     -- env WINIX_AGENTS_HOME="$AGHOME" "$WINIX_EXE" agents init
smoke A06 "agents remove (user) -> 0"              -- env WINIX_AGENTS_HOME="$AGHOME" "$WINIX_EXE" agents remove
smoke A07 "agents status after remove -> drift 1"  -- env WINIX_AGENTS_HOME="$AGHOME" "$WINIX_EXE" agents status

# Empty home (no .claude/.codex, no force) -> usage 125.
AGEMPTY="$RES/agents-home-empty"; rm -rf "$AGEMPTY"; mkdir -p "$AGEMPTY"
smoke A08 "agents init no home no force -> 125"     -- env WINIX_AGENTS_HOME="$AGEMPTY" "$WINIX_EXE" agents init
smoke A09 "agents init --codex force-creates -> 0"  -- env WINIX_AGENTS_HOME="$AGEMPTY" "$WINIX_EXE" agents init --codex

# Project scope (opt-in) — committed-file behaviour, conditional wording.
smoke A10 "agents init --project writes AGENTS.md -> 0" -- "$WINIX_EXE" agents init --project --path "$AGREPO"
smoke A11 "agents status --project current -> 0"        -- "$WINIX_EXE" agents status --project --path "$AGREPO"
smoke A12 "agents init --project --claude both -> 0"    -- "$WINIX_EXE" agents init --project --path "$AGREPO2" --claude
smoke A13 "agents init --project --dry-run -> 0"        -- "$WINIX_EXE" agents init --project --path "$AGREPO" --dry-run

# Validation errors.
smoke A14 "agents --path without --project -> 125" -- "$WINIX_EXE" agents status --path "$AGREPO"
smoke A15 "agents --project with --codex -> 125"   -- "$WINIX_EXE" agents init --project --codex --path "$AGREPO"

# Capture rendered blocks for inspection (user assert vs project conditional).
cp "$AGHOME/.claude/CLAUDE.md" "$RES/A.user.CLAUDE.md.txt" 2>/dev/null || true
cp "$AGREPO2/AGENTS.md" "$RES/A.project.AGENTS.md.txt" 2>/dev/null || true
```

> Verify-at-implementation: the `smoke` helper's expected-exit-code convention is encoded in the description text only (e.g. "-> 0", "-> 125") in this fixture — confirm whether it parses the arrow or always expects 0, and adjust the non-zero cases (A01, A07, A08, A14, A15) to whatever mechanism the helper uses for expected-nonzero (the pre-edit A09/A10/A11/A12 used description-only). If the helper always expects 0, wrap nonzero cases as the file already does for A01/A09-A12.

- [ ] **Step 2: Lint the script**

Run: `bash -n artifacts/round-stop-2026-05-09/winix/run-smokes.sh`
Expected: no syntax errors.

- [ ] **Step 3: Commit**

```bash
git add artifacts/round-stop-2026-05-09/winix/run-smokes.sh
git commit -m "test(smoke): agents user-scope (env-isolated) + project/codex/empty-home cases"
```

---

## Task 10: Regenerate the `--describe` contract snapshot

**Files:**
- Regenerate: `tests/Winix.Contract.Tests/snapshots/winix.describe.json`

- [ ] **Step 1: Confirm the snapshot currently fails (new flags not yet captured)**

Run: `dotnet test tests/Winix.Contract.Tests/Winix.Contract.Tests.csproj --filter "FullyQualifiedName~winix"`
Expected: FAIL — `--describe` now emits `--project`/`--codex` and changed descriptions vs the committed snapshot.

- [ ] **Step 2: Regenerate in update mode**

Run: `WINIX_UPDATE_SNAPSHOTS=1 dotnet test tests/Winix.Contract.Tests/Winix.Contract.Tests.csproj --filter "FullyQualifiedName~winix"`
Expected: the test fails by design ("snapshot regenerated … commit the diff") but rewrites `snapshots/winix.describe.json`.

- [ ] **Step 3: Inspect the diff for sanity**

Run: `git diff tests/Winix.Contract.Tests/snapshots/winix.describe.json`
Expected: only additions/edits for `--project`, `--codex`, the reworded `--claude`/`--path`, and the new examples — nothing unrelated.

- [ ] **Step 4: Re-run without update mode, verify pass**

Run: `dotnet test tests/Winix.Contract.Tests/Winix.Contract.Tests.csproj --filter "FullyQualifiedName~winix"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tests/Winix.Contract.Tests/snapshots/winix.describe.json
git commit -m "test(contract): regenerate winix --describe snapshot for agents scope flags"
```

---

## Task 11: Docs — README, man page, agent guide, llms.txt

**Files:**
- Modify: `src/winix/README.md` (`agents` section ~105-165, examples ~79-86, exit codes ~196-199)
- Modify: `src/winix/winix.1.md` → regenerate `src/winix/man/man1/winix.1`
- Modify: `docs/ai/winix.md`, `llms.txt`

- [ ] **Step 1: Confirm the man page has a pandoc source (repo-wide listing — never a per-tool guess)**

Run: `git ls-files '*.1.md'`
Expected: includes `src/winix/winix.1.md` → edit the `.md`, regenerate the `.1`. (Confirmed present during planning.)

- [ ] **Step 2: Rewrite the README `agents` section**

Update the intro paragraph (line 107) to lead with user scope, the verbs table, the options table (add `--project`, `--codex`; reword `--claude`/`--path`), the examples (line 79-86, 152-165), and the exit-code note. Replace the bootstrap idiom with the user-scope form:

```bash
# Bootstrap: write the pointer to your user agent config if absent or stale
winix agents status || winix agents init

# Force-create a specific home that doesn't exist yet
winix agents init --codex

# Team opt-in: a conditional pointer committed into the repo
winix agents init --project
```

Add a **Migration** note (Review F6 — the default `status` now inspects your user home, so it will NOT detect a block previously committed into a repo; the note must say so explicitly or migration is documentation theatre):

> **Migration (v0.4.0):** the default scope changed from the project directory to your user agent config. **`winix agents status` (default) no longer looks at repo files** — so if you ran a pre-release build that wrote a block into a project's `AGENTS.md`/`CLAUDE.md`, default status will not surface it. In any repo where you previously ran `winix agents init`, run `winix agents status --project` to detect the committed block and `winix agents remove --project` to clear it.

Add a **Known limitations** subsection (README + `docs/ai/winix.md`):

> - **(F3) Multi-target writes are not atomic across files.** `init`/`remove` write each target independently; on a mid-run I/O error, already-written targets are kept and the command exits non-zero naming the failing target. Re-run to converge — the operation is idempotent.
> - **(F2) The managed block records its version, not its wording mode.** A block at the current version is reported `current` even if it carries the other scope's wording (e.g. a project-mode block hand-placed in a user home). Status detects version drift, not wording drift; `remove` + `init` re-writes the correct wording.

- [ ] **Step 3: Edit the man source and regenerate**

Edit `src/winix/winix.1.md` to mirror the README changes, then:

Run: `pandoc -s -t man src/winix/winix.1.md -o src/winix/man/man1/winix.1`
Expected: regenerated groff.
Run: `git diff src/winix/man/man1/winix.1`
Expected: only the agents-scope content changes (+ pandoc reflow) — no unrelated churn.

- [ ] **Step 4: Update `docs/ai/winix.md` and `llms.txt`**

Reflect user-default scope, `--project` opt-in, `--codex`, and the empty-home behaviour in both. Keep `llms.txt` to the one-line-per-surface house style.

- [ ] **Step 5: Commit**

```bash
git add src/winix/README.md src/winix/winix.1.md src/winix/man/man1/winix.1 docs/ai/winix.md llms.txt
git commit -m "docs(agents): document user-default scope, --project opt-in, --codex, empty-home"
```

---

## Task 12: Doc↔behaviour reconciliation (ship gate) + full suite

**Files:** none (verification only) — fix-forward any drift found.

- [ ] **Step 1: Build the AOT-free debug binary for manual probing**

Run: `dotnet build src/winix/winix.csproj`
Expected: SUCCESS.

- [ ] **Step 2: Enumerate every user-facing claim and run the command that demonstrates it**

For each claim across `winix --help`, `winix agents` (no verb) usage, `agents --describe` (via `winix --describe`), README, man, `docs/ai/winix.md`, `llms.txt`, run the matching command against a `WINIX_AGENTS_HOME` scratch dir and confirm the behaviour. Actively hunt for the FALSE claim. Minimum probes (use a scratch home so your real `~/.claude` is untouched):

```bash
export WINIX_AGENTS_HOME="$(mktemp -d)"; mkdir -p "$WINIX_AGENTS_HOME/.claude"
dotnet run --project src/winix -- agents init           # writes ~/.claude/CLAUDE.md, assert wording
dotnet run --project src/winix -- agents status          # current -> exit 0
dotnet run --project src/winix -- agents init --project --path "$(mktemp -d)"  # conditional wording
WINIX_AGENTS_HOME="$(mktemp -d)" dotnet run --project src/winix -- agents init # empty home -> 125
dotnet run --project src/winix -- agents status --path .  # --path w/o --project -> 125
```

Confirm: user block contains "(available on this machine)"; project block contains "(if available in your environment)" and "If Winix is not installed, ignore"; empty-home prints "no agent home found"; both validation errors exit 125.

- [ ] **Step 3: Run the entire solution test suite**

Run: `dotnet test Winix.sln`
Expected: 0 failures across all projects.

- [ ] **Step 4: Commit any doc fixes from Step 2**

```bash
git add -A
git commit -m "docs(agents): reconcile help/README/man/llms against actual behaviour"
```

---

## Self-Review (completed by plan author)

- **Spec coverage:** §2 scope model → Tasks 1,5,6,8. §3 CLI surface → Task 8. §4 empty-home edge + validation → Tasks 5,6,8. §5 home resolution/override → Tasks 3,4. §6 wording divergence → Tasks 1,2. §7 status/remove → Task 6. §8 migration note → Task 11. §9 surfaces → Tasks 9,10,11. §11 reconciliation gate → Task 12. All covered.
- **Type consistency:** `RenderMode {UserScope,ProjectScope}`, `AgentsScope {User,Project}`, `AgentHome(Id,Dir,File)`, `KnownHomes`, `ResolveUserTargets`, `TryResolveTargets`, `ResolveHome`/`CreateDirectory` used identically across tasks. `AgentsOptions` 8-arg shape (Verb, Scope, BaseDir, ForceClaude, ForceCodex, DryRun, Json, Version) consistent in Tasks 3,5,6,7,8.
- **Placeholder scan:** the two "verify-at-implementation" notes (Task 4 path separators, Task 8 `Cli.RunAsync` signature, Task 9 smoke-helper exit convention) are deliberate flags for the implementer to confirm against source, not unfilled gaps — each names exactly what to check and why.
- **Known caller-audit risk:** changing `AgentsOptions`, `RenderBlock`, `MergeBlock` arity breaks existing test callers; Task 7 is the dedicated caller-migration step, and Tasks 5/6 note in-step that old project tests must adopt `AgentsScope.Project`.

## Adversarial Review Integration (2026-06-09)

A fresh-subagent adversarial review (15-category taxonomy) produced 3 blockers / 5 test gaps / 4 defers. Disposition:

- **F1** (`WINIX_AGENTS_HOME` non-dir/relative) — integrated: `Path.GetFullPath` normalisation + contract doc + non-existent-home test (Task 3, Task 5). Downgraded from blocker (test-only knob; file-collision already maps to clean `InternalError`).
- **F2** (render-mode-vs-version drift reported `current`) — integrated as accepted **known limitation** + pinning test (Task 6) + README/agent-guide note (Task 11). Chose docs over a marker mode-token (would break the byte-stable parser).
- **F3** (multi-target non-atomic partial commit) — integrated: known-issues note (Task 11).
- **F4** (no I/O-error-mid-write test) — integrated: `FakeFs.ThrowOnWritePath` + `RunInit_WriteFailure` test (Tasks 3, 5).
- **F5** (smoke expected-exit-code mechanism unverified — **BLOCKER**) — integrated: mandatory Task 9 **Step 0** + known-nonzero control before any case is trusted.
- **F6** (migration orphans committed block; default status won't surface it) — integrated option (a): explicit README note that default status doesn't inspect repos + `status --project` discovery step (Task 11). Downgraded from blocker (feature shipped only on untagged `release/v0.4.0` — no released binary, migration population ≈ this repo's dev; proactive CWD-peek hint rejected as over-scope).
- **F7** (malformed-marker tests under new modes) — verified existing suite already pins this (`FindBlockVersion_StartWithoutEnd_ReturnsNull`, `RemoveBlock` duplicate-strip); added one cross-mode belt-and-braces test (Task 6).
- **F8** (empty-home `status --json` ambiguity) — integrated: shape-pinning test (Task 6); distinguishing field deferred as a documented conscious choice.
- **F9** (force-create dir not deterministically unit-tested) — integrated: `RunInit` creates dir via the `CreateDirectory` seam; restored the `fs.Created` assertion; removed the self-defeating "drop the assertion" correction (Task 5).

Single review pass; findings converged and integrated — no second pass required (changes are additive tests + docs + one small seam call, no structural plan change).
```
