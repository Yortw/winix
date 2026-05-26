# Release-pipeline cleanup — design

- **Date:** 2026-05-26
- **Status:** Approved (design)
- **Branch:** `release/v0.4.0` (one of several v0.4.0 work items, not the whole release)
- **Companion ADR:** `2026-05-26-release-pipeline-cleanup-adr.md`

## Context

v0.3.0 shipped 2026-05-26 across NuGet / GitHub / scoop. Inspection of the **actual shipped
release artifacts** (not reproductions) surfaced three defects in the artifacts and one
long-standing cosmetic defect in `--version` output:

1. **`when` ships no man page on any platform.** `src/when/when.csproj` lacks the
   `<Content Include="man\man1\when.1" …>` line that the other 22 tools have. Confirmed by
   grepping all 23 tool csprojs (22 have it, only `when` does not) and by inspecting
   `when-win-x64.zip` (binary + pdbs, no `share/`). This is **not** a class — it is a single
   omission.

2. **No tool ships a man page on Linux/macOS.** The per-tool *nix zip step runs
   `zip -j <archive> *`, which has no `-r`, so `zip` never descends into the `share/`
   subdirectory where the man page lives. Confirmed from shipped bytes:
   `timeit-linux-x64.zip` contains `timeit`, `timeit.dbg`, two managed `.pdb`s — **no man
   page**; `timeit-win-x64.zip` (built with `Compress-Archive`, which recurses) **does**
   contain `share/man/man1/timeit.1`. Even the `man` tool's Linux zip — which the pipeline
   tries to fill with *all* pages via a dedicated bundling step — ships zero pages for the
   same reason. The `-j` flatten suspected in earlier notes is a red herring: the page is
   never captured at all, flatten or not.

3. **Debug symbols dominate every download.** Measured from shipped zips:
   - `timeit-win-x64.zip`: native `timeit.pdb` is **10.4 MB of a 12.3 MB zip (~85%)**, plus
     two managed `.pdb`s.
   - `timeit-linux-x64.zip`: `timeit.dbg` is **4.7 MB of 7.3 MB (~64%)**, plus two managed
     `.pdb`s.
   - `timeit-osx-arm64.zip`: **2.42 MB binary + ~41 KB of managed `.pdb`s only** — there is
     **no separate native symbol file** on macOS (NativeAOT embeds or omits native debug
     info there). macOS therefore has no separable symbol weight worth chasing.

4. **`--version` emits `X.Y.Z+<gitsha>` on 13 tools.** The SDK appends the commit SHA to
   `AssemblyInformationalVersion`; 9 newer tools strip it in `ResolveVersion()`, 13 older ones
   do not.

## Goal

Future releases ship complete, lean artifacts, and `--version` is clean suite-wide. No change
to the v0.3.0 release (immutable); fixes land for the next tag.

## Components

### 1. `when` man page (csproj)

Add the standard line to `src/when/when.csproj`:

```xml
<Content Include="man\man1\when.1" CopyToPublishDirectory="PreserveNewest" Link="share\man\man1\when.1" />
```

Single-file change. The man page source (`src/when/man/man1/when.1`) already exists and
already documents the `now` keyword; it is simply never copied to publish output today.

### 2. *nix man pages in zips (recursion)

In `release.yml` "Zip binaries (Linux/macOS)", stop using `zip -j … *` (junk paths, no
recurse). Zip **with recursion**, preserving the relative `share/man/man1/<tool>.1` path so
the *nix archive layout matches Windows: binary at the zip root, man page under `share/`.

### 3. Symbols → strip from main zip + separate `-symbols` artifact

Decision A (see ADR). Per tool, per RID, in the zip steps:

1. Capture symbol files into `<tool>-<rid>-symbols.zip`: `*.pdb` (all platforms) and `*.dbg`
   (Linux). macOS yields only the managed `.pdb`s (~41 KB) — uniform handling, negligible win.
2. Remove the captured symbol files from the publish directory.
3. Zip the remainder (binary + `share/`) into the existing `<tool>-<rid>.zip`.
4. Upload both the main zips and the `-symbols` zips as release assets.

**Implementation shape — loop, not 23 hand-listed lines.** The two per-tool zip steps
(Linux/macOS bash, Windows pwsh) are replaced with a loop over a tool-name array (a bash array
in the *nix step, a pwsh array in the Windows step). This is the minimal way to apply
capture→strip→zip uniformly and removes the per-tool drift that let bug #1/#2 hide. The
combined `winix-<rid>.zip` step already copies only `.exe` files + the `share/` tree, so it
stays symbol-free with no change.

Windows capture/strip mechanism: `Compress-Archive` has no exclude, so capture pdbs into the
symbols zip first, `Remove-Item` the pdbs from the publish dir, then `Compress-Archive` the
remainder (this preserves the `share/` subtree the same way the current step does). *nix:
`zip -j` the symbols, `rm` them, then `zip -r` the remainder.

### 4. `+gitsha` drift (one line, suite-wide)

Add to `Directory.Build.props`:

```xml
<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
```

Stops the SDK appending `+<sha>`. The 9 tools that already strip become harmless no-ops
(`IndexOf('+')` returns -1). Trade-off: the commit SHA no longer appears in assembly metadata.

> **Execution finding (2026-05-26): premise falsified — retained as a forward-guard.** During
> implementation, the shipped v0.3.0 binaries were inspected: `timeit 0.3.0` and `when 0.3.0`
> (both on the memory's "doesn't strip" list) print clean `X.Y.Z` with **no** `+sha`. The repo
> has no SourceLink, so the SDK never populates `SourceRevisionId` and never appends the suffix
> — the `+gitsha` "drift" does not actually manifest. The 29-day-old memory
> (`project_version_strip_drift`) was stale. Per user decision the property is kept anyway as a
> **forward-guard** (a no-op today that prevents a future SourceLink addition from reintroducing
> the suffix), not as an active-bug fix. Commit `8f815cc`.

## Out of scope (explicit)

- **NuGet package managed pdbs (~52 KB).** Tiny, and they aid stack-trace symbolication on the
  framework-dependent JIT tool path. Decision A keeps symbols *generated*, so there is no
  global `DebugType=none`/`DebugSymbols=false` (which would also strip the NuGet pdbs and test
  symbols). Left as-is.
- **macOS embedded native symbols.** No separate file exists to strip; `dsymutil`/`strip` is
  more involved and the macOS download is already ~2.4 MB. Negligible win, deferred.
- **Other v0.4.0 feature work.** This branch will carry more than this cleanup; this spec
  covers the pipeline cleanup item only. No tagging/finalising of v0.4.0 here.

## Verification

- **Local — version drift:** build, then run `--version` on a couple of the 13 affected tools;
  assert output has no `+`-suffix.
- **Local — Windows zip logic:** run the new pwsh capture/strip/zip against a synthetic publish
  directory (binary + `share/man/man1/x.1` + fake `*.pdb`); assert the main zip contains the
  binary and man page and **no** pdb, and the symbols zip contains the pdbs. (`zip` is not
  installed on the dev box, so the bash path cannot be exercised locally.)
- **Ship gate — draft-release inspection:** `release.yml` creates the GitHub release as a
  `--draft`. Before publishing the v0.4.0 draft, download a sample of `*-<rid>.zip` and
  `*-symbols.zip` across all four RIDs and confirm: man pages present on every platform
  including `when`; no symbols in the main zips; symbols present in the `-symbols` zips.

## Risks

- **Loop refactor blast radius:** a mistake breaks all tools at once rather than one. Mitigated
  by the local pwsh test and the draft-release inspection gate.
- **Bash path unverifiable locally** (no `zip` binary on the dev machine): relies on careful
  construction plus the draft-release gate. Info-ZIP `-r`/`-j` semantics are stable across
  platforms, so the risk is in the script, not the tool.
