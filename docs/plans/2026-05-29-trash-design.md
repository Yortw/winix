# trash — design

**Date:** 2026-05-29
**Status:** Proposed
**Tool:** `trash` — cross-platform safe-delete to the OS recycle bin / Trash.
**Companion ADR:** [2026-05-29-trash-adr.md](2026-05-29-trash-adr.md)

## Purpose

A single cross-platform CLI that moves files/directories to the OS recycle bin / Trash
instead of deleting them permanently — and lets you see what's in the bin and empty it.
Windows is the real gap (no built-in recycle-bin CLI; `Remove-Item` deletes permanently);
Linux and macOS have established tools we should *match*, not undercut.

## Landscape

- **`trash-cli`** (andreafrancia, Python, GPL) — canonical on Linux. Implements the
  FreeDesktop.org Trash spec, including full multi-volume support. No Windows. We interoperate
  with it by implementing the same spec (shared trash dirs → our `--list` sees its items and
  vice-versa, as do GUI file managers).
- **`macos-trash`** (sindresorhus, Swift) — the modern macOS approach: a thin wrapper around
  `NSFileManager.trashItem`. Gives native "Put Back" and per-volume routing without Finder.
  (The older `ali-rantakari/trash` used Finder/ScriptingBridge; the ecosystem moved on.)
- **`trash`** (sindresorhus, Node) — genuinely cross-platform; shells to `macos-trash` on mac.
  Closest prior art. Our edge is runtime/dependency weight (one AOT binary, no Node/Python) and
  suite-consistent `--json`/`--describe` — **not** "needs install" (we do too).

**Positioning:** on Linux/mac we are not filling a gap; the pitch is one binary, no runtime
dependency, and the *same* tool/behaviour on Windows where there is a genuine gap. The README
must acknowledge `trash-cli` and `macos-trash` as the established native options.

## Scope (v1)

| Command | Behaviour |
|---|---|
| `trash <path>...` | Send one or more files/dirs to the OS recycle bin / Trash (default action). |
| `trash --list` | List what's in the trash (read-only). |
| `trash --empty` | Permanently empty the trash (guarded — see Safety). |

**Deferred to v2:** `--restore`. See ADR for why (cross-platform indexing + macOS original-path
retrieval are the costly parts).

## Architecture

Follows the suite split:

- **`Winix.Trash`** (class library) — all logic + formatting.
- **`trash`** (console app) — thin: arg parsing, validation, I/O, exit code.

```
trash/Program.cs ──> Winix.Trash.Cli.Run(args, stdout, stderr, backend?)
                          │
                          ├─ ArgParser (ShellKit CommandLineParser, flag-mode)
                          ├─ TrashBackendFactory.Create() ──> ITrashBackend
                          │       ├─ WindowsRecycleBinBackend   (SHFileOperationW / $I parse)
                          │       ├─ LinuxFreeDesktopBackend     (spec dirs + .trashinfo)
                          │       └─ MacOsTrashBackend           (NSFileManager.trashItem via objc)
                          └─ Formatting (table, JSON envelope, stderr summary)
```

`ITrashBackend`:

```csharp
public interface ITrashBackend
{
    TrashResult Trash(IReadOnlyList<string> paths);   // per-path success/failure
    IReadOnlyList<TrashedItem> List();                 // enumerate the bin
    EmptyResult Empty();                               // permanent
}
```

`Cli.Run(string[] args, TextWriter stdout, TextWriter stderr, ITrashBackend? backendOverride = null)`
mirrors the mksecret `ISecureRandom?` seam: production passes `null` (real backend via factory),
tests inject a fake backend to exercise orchestration deterministically.

**Arg parsing:** ShellKit `CommandLineParser`, flag-mode (not subcommands). Bare positionals =
trash; `--list` and `--empty` are mode flags, mutually exclusive with each other and with
positional paths. Mirrors the memory-captured shape (`trash <paths>`, `trash --list`, etc.) and
keeps the common case ergonomic (`trash file.txt`, not `trash put file.txt`).

## Per-OS backends

### Windows — `WindowsRecycleBinBackend`
- **Trash:** `SHFileOperationW` with `FO_DELETE | FOF_ALLOWUNDO | FOF_NOCONFIRMATION |
  FOF_SILENT | FOF_NOERRORUI`. Routes to the correct per-drive `$Recycle.Bin` automatically →
  **multi-volume is free**. AOT-clean `LibraryImport`; no COM.
- **Empty:** `SHEmptyRecycleBinW`.
- **List:** parse `<drive>\$Recycle.Bin\<SID>\$I*` metadata files (v2 format: 8-byte header,
  8-byte size, 8-byte deletion FILETIME, 4-byte path length, UTF-16 original path). No COM.
  Enumerate the current user's SID folder across accessible fixed drives.

### Linux — `LinuxFreeDesktopBackend`
- **Trash:** FreeDesktop spec. Move into `~/.local/share/Trash/files/`, write a matching
  `info/<name>.trashinfo` (`[Trash Info]`, URL-encoded `Path=`, ISO-8601 `DeletionDate=`).
  Name collisions get a numeric suffix. **Full multi-volume:** for a file on another mount,
  resolve the mount top dir and use `$topdir/.Trash-$uid` (with the spec's sticky-bit/symlink
  safety checks), matching `trash-cli`.
- **Empty:** clear `files/` + `info/` across all our trash dirs.
- **List:** read `.trashinfo` across trash dirs → original path + deletion date + size.

### macOS — `MacOsTrashBackend`
- **Trash:** `NSFileManager.trashItem` via `objc_msgSend` P/Invoke (`/usr/lib/libobjc`). Gives
  native **Put Back** and per-volume routing → **multi-volume is free**. This is the suite's
  **first native Objective-C interop** (the Keychain backend shells to `security`); written
  carefully and verified on the macOS CI runner.
- **Empty:** remove contents of `~/.Trash` (and `/Volumes/*/.Trashes/$uid`). (No public
  "empty trash" Foundation API; Finder owns that — so we clear the dirs ourselves.)
- **List:** enumerate `~/.Trash` (+ volume `.Trashes`) → names + deletion dates + sizes.
  **Original paths are NOT available** (the Put-Back source lives in macOS's private store with
  no public read API) — see Known Limitations.

## Behaviour & safety

- **Trashing is recoverable → no confirmation.** Multiple paths: attempt all, report per-path
  failures, exit non-zero if any failed (like `rm`). Symlinks: trash the link, not the target.
  Directories: recursive (all bins handle trees). A missing path errors for that path and
  processing continues.
- **`--empty` is permanent → safe by default.** Prompt `Permanently delete N item(s)? [y/N]`
  when stdin is a TTY; require **`--yes`** to proceed when non-interactive (scripts/pipes). This
  prevents an accidental scripted mass-wipe.
- **Exit codes:** `0` success (a closed downstream pipe is also `0` — not an error); `125`
  usage error; `1` when one or more paths failed for an operational reason (missing path,
  permission denied) while others may have succeeded — like `rm`; `126` when the backend itself
  cannot run (OS API failure, recycle bin unavailable).

## Output / `--json`

- **Trash:** summary to **stderr** (`trash: moved 3 item(s) to trash`); nothing to stdout unless
  `--json`. Keeps stdout clean for piping.
- **`--list`:** human table to **stdout**; `--json` →
  `{"items":[{"name":...,"original_path":...?,"deleted":ISO8601,"size":bytes?,"trash":"home|/mnt/x"}...]}`
  to stdout. `original_path` omitted where unavailable (macOS).
- **`--empty`:** summary to stderr; `--json` result envelope (`{"emptied":N}`) to stdout.
- `--json` available in all modes (suite convention). `NO_COLOR` respected. ANSI/UTF-8 set up
  front in `Program.cs`.

## Known v1 limitations (documented in README + man + AI-guide + known-issues)

1. **macOS `--list` shows names + deletion dates, not original paths.** The Put-Back source is in
   macOS's private store with no public read API. Windows (`$I`) and Linux (`.trashinfo`) `--list`
   *do* carry original paths. (Put Back itself works on macOS — we use `trashItem`.)
2. **Windows glob expansion:** cmd/pwsh don't expand `*`; relies on the deferred suite-wide
   ShellKit fix. Pass explicit paths meanwhile (consistent with digest/files/squeeze/etc.).
3. **`--restore` is not in v1** (see ADR).

## Testing strategy (first-class — three native backends, two not runnable on the dev box)

**Unit (run everywhere incl. the Windows dev box + WSL):**
- Arg parsing: paths, `--list`/`--empty`/`--yes`/`--json`, mode mutual-exclusion, errors.
- Formatting: list table, JSON envelopes (list/trash/empty), stderr summary lines.
- **`.trashinfo` writer + parser** and **Windows `$I` parser** pinned against **literal
  byte/text fixtures captured from real recycle bins** — *not* round-trip through our own encoder
  (the protocol-fake trap: round-trip alone passes even with a wrong wire format).
- Linux mount-point / top-dir resolution as a pure helper with injected device/mount info.
- Backend factory; exit-code mapping.
- `Cli.Run` with an injected **fake `ITrashBackend`**: orchestration, the `--empty` prompt/`--yes`
  gating, JSON shape, exit codes — without touching the real trash.

**Integration (`SkippableFact` + `Skip.IfNot` per suite convention; deterministic — no
signal/timing races; self-cleaning):**
- **Windows** (dev box + CI): temp file → `trash` → assert gone from origin + matching `$I`/`$R`
  pair exists → remove them. `--list` shows it.
- **Linux** (WSL + CI): temp file → `trash` → assert moved to `~/.local/share/Trash/files/` +
  correct `.trashinfo` → self-clean. Multi-volume: tmpfs/bind-mount → assert `$topdir/.Trash-$uid`.
- **macOS** (CI-only): temp file → `trash` → assert origin gone + present in `~/.Trash` →
  self-clean. **This proves the Obj-C interop actually works** — the safety net for the one
  backend not runnable locally.

**Deliberate test gates (documented, not silently skipped):**
- **`--empty` is NOT exercised against the real trash** — the OS empty APIs are all-or-nothing and
  would wipe unrelated items. Verified via the fake backend (asserts `Empty()` invoked) + the
  prompt/`--yes` gating at unit level + manual smoke. This is a ship-gate, like the protocol
  wire-correctness gates elsewhere in the suite.
- Every integration test asserts on **filesystem state**, never timing/signals (explicitly
  avoiding the flaky-test class seen in nc/timeit).

## Suite wiring (per CLAUDE.md "adding a new tool")

NuGet `Winix.Trash`; `bucket/trash.json`; `src/Winix.Trash/` + `src/trash/`;
`tests/Winix.Trash.Tests/`; `src/trash/README.md` + `man/man1/trash.1`; `docs/ai/trash.md`;
`llms.txt` entry; release.yml + post-publish.yml entries; CLAUDE.md layout/IDs/manifests lists.
CHANGELOG at first stable tag only.
