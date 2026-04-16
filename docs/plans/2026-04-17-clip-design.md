# clip — Cross-Platform Clipboard Bridge

**Date:** 2026-04-17
**Status:** Proposed
**Project:** Winix (`D:\projects\winix`)

---

## Overview

`clip` is a cross-platform CLI tool that reads from and writes to the system clipboard using one consistent interface. It replaces the patchwork of `pbcopy`/`pbpaste` (macOS), `xclip`/`xsel`/`wl-copy`/`wl-paste` (Linux), and the write-only `clip.exe` (Windows) with a single command whose behaviour is identical across platforms.

**Why it's needed:**

- **Windows** has no native clipboard *paste* command. `System32\clip.exe` is write-only. Shell users end up reaching for PowerShell (`Get-Clipboard`) or third-party tools to paste.
- **Linux** has four viable helpers (`xclip`, `xsel`, `wl-copy`, `wl-paste`) with different flag syntaxes, and picking the right one depends on whether the session is X11 or Wayland. A script written for one distro often breaks on another.
- **macOS** has `pbcopy` and `pbpaste`, which work well for plain text but require two separate commands and can't sensibly round-trip rich types.
- **The suite needs it.** Cross-tool composability like `pass --copy`, `qr ... | clip`, `ids | clip`, and `digest ... | clip` depends on a tool literally named `clip` existing on `PATH` on every platform.

**Honest positioning:**

- **Windows:** genuine gap-fill — first-class paste and clear support for Windows shell users.
- **Linux:** papers over the X11/Wayland + three-helper-tool choice. Real but narrower value.
- **macOS:** in v1, mostly a wrapper for suite coherence. v2 (format support, images) is where macOS genuinely wins, since `pbcopy`/`pbpaste` can't do rich types.

**Primary use cases (v1):**

- Paste clipboard contents into a pipeline: `clip | grep foo`
- Copy command output to clipboard: `cat file.txt | clip`
- Copy a password-generator result: `ids | clip`
- Clear the clipboard after handling a secret: `clip --clear`
- Force a mode in a script where stdin state is uncertain: `clip --paste` / `clip --copy`

**Platform:** Cross-platform (Windows, Linux, macOS). Backend selected at runtime.

---

## Project Structure

```
src/Winix.Clip/         — class library (backends, options, arg parser, formatting)
src/clip/               — thin console app (arg parsing, autodetect, I/O)
tests/Winix.Clip.Tests/ — xUnit tests
bucket/clip.json        — scoop manifest
docs/ai/clip.md         — AI agent guide
```

Standard Winix conventions: library does all work, console app is thin.

---

## CLI Interface

```
clip                     # paste (read clipboard to stdout)
clip < file.txt          # copy from file (stdin redirected)
echo foo | clip          # copy from pipe
clip --clear             # empty the clipboard
clip -c / --copy         # force copy mode (override autodetect)
clip -p / --paste        # force paste mode
clip -r / --raw          # paste raw — do not strip trailing newline
clip --primary           # target X11/Wayland PRIMARY selection (Linux only; ignored elsewhere)
clip --describe          # emit self-description JSON (AI discoverability)
clip --help
clip --version
```

### Mode Autodetection

Pure function `ResolveMode(bool stdinRedirected, ClipOptions opts) → ClipMode`:

1. If `--clear` present → `Clear`.
2. If `--copy` present → `Copy`.
3. If `--paste` present → `Paste`.
4. If stdin is redirected → `Copy`.
5. Otherwise → `Paste`.

Conflicting flags (e.g. `-c` with `-p`, `--clear` with `-c`, `--clear` with redirected stdin) are rejected at parse time with exit 125.

### Newline Handling

- **Copy:** stdin bytes are treated as UTF-8 and written to the clipboard unchanged. No normalisation. (Windows apps that require CRLF handle LF just fine in modern use; forcing CRLF would surprise users piping binary-adjacent text.)
- **Paste:** by default, strip exactly one trailing `\n` or `\r\n`. This mirrors `$(...)` shell behaviour and the `wl-paste --no-newline` default. Use `--raw` to preserve every byte. Multi-line content retains internal newlines — only the final one is stripped.

**Documentation requirement:** this asymmetry (copy preserves bytes, paste strips one trailing newline by default) MUST be called out prominently in `src/clip/README.md` — ideally with a short "Newline handling" section right after the basic usage examples — including the `--raw`/`-r` override. It's the one place `clip` is opinionated about content, and users hitting a byte-inexact round-trip without knowing why will be confused. Also mention it in the man page and the AI guide.

### Primary Selection (Linux)

On Linux, X11 and Wayland each expose two selections: `CLIPBOARD` (the standard Ctrl+C/V one) and `PRIMARY` (middle-click / last-selected-text). Default targets `CLIPBOARD`. `--primary` targets `PRIMARY`. On Windows and macOS the flag is silently ignored (documented behaviour) because those platforms have no equivalent concept.

### Self-Description (`--describe`)

Emits a JSON document describing the tool's purpose, flags, exit codes, and examples. Matches the pattern used by every other Winix tool for AI discoverability.

---

## Architecture

### Class Library (`Winix.Clip`)

**Core types:**

- `ClipMode` — enum: `Copy`, `Paste`, `Clear`.
- `ClipOptions` — parsed flags (mode, raw, primary, describe, help, version).
- `ClipResult` — exit code + optional error message.
- `IClipboardBackend` — interface:
  - `void CopyText(string text)`
  - `string PasteText()` — returns empty string on empty clipboard (no exception).
  - `void Clear()`
- `ClipboardBackendFactory` — selects a backend based on platform probe results.
- `IPlatformProbe` — abstraction for OS detection, env var access, PATH probing. Real implementation in library; tests inject fakes.
- `IProcessRunner` — abstraction over `Process.Start` for shell-out backends. Real implementation in library; tests inject fakes.
- `ArgParser` / `Formatting` — pure functions for parsing and for composing error/usage output.

**Backends:**

- **`WindowsClipboardBackend`** — direct P/Invoke against `user32.dll`:
  - `OpenClipboard`, `EmptyClipboard`, `SetClipboardData(CF_UNICODETEXT, …)`, `GetClipboardData(CF_UNICODETEXT)`, `CloseClipboard`, `IsClipboardFormatAvailable`, `GlobalAlloc`, `GlobalLock`, `GlobalUnlock`, `GlobalFree`.
  - Strings are marshalled UTF-16LE (matching `CF_UNICODETEXT`) at the boundary; the public API stays `string`.
  - `OpenClipboard` is retried up to 5 times at 50 ms intervals when another process briefly holds the clipboard (common on Windows). After exhaustion, surfaces a "clipboard busy" error.
  - AOT-safe: no reflection, explicit marshalling.

- **`ShellOutClipboardBackend`** — generic backend parameterised by a `ClipboardHelperSet`:
  - `ClipboardHelperSet` record — binary names + arg vectors for copy, paste, and clear.
  - Always uses `ProcessStartInfo.ArgumentList` (per project convention; avoids trailing-backslash quoting bugs).
  - Stdin streamed into the helper for copy; stdout captured for paste.
  - Non-zero helper exit → raised as error, helper stderr passed through.

- **Helper sets:**
  | Name | Copy | Paste | Clear |
  |---|---|---|---|
  | `wl-clipboard` | `wl-copy` (stdin) | `wl-paste --no-newline` (stdout) | `wl-copy --clear` |
  | `xclip` | `xclip -selection clipboard -i` (stdin) | `xclip -selection clipboard -o` (stdout) | `xclip -selection clipboard -i` (empty stdin) |
  | `xsel` | `xsel --clipboard --input` (stdin) | `xsel --clipboard --output` (stdout) | `xsel --clipboard --clear` |
  | `pb` (macOS) | `pbcopy` (stdin) | `pbpaste` (stdout) | `pbcopy` (empty stdin) |

  `--primary` swaps `--selection clipboard` → `--selection primary` for `xclip`, `--clipboard` → `--primary` for `xsel`, and adds `--primary` for `wl-copy`/`wl-paste`. On `pb` it has no effect (documented).

  `wl-paste --no-newline` is used so our own newline-stripping stays the single source of truth regardless of helper quirks.

### Backend Selection

`ClipboardBackendFactory.Create(IPlatformProbe probe)`:

1. `probe.Os == Windows` → `WindowsClipboardBackend`.
2. `probe.Os == MacOS` → `ShellOutClipboardBackend(helpers: pb)`.
3. `probe.Os == Linux`:
   a. `probe.GetEnv("WAYLAND_DISPLAY")` non-empty and `probe.HasBinary("wl-copy")` → `wl-clipboard`.
   b. `probe.HasBinary("xclip")` → `xclip`.
   c. `probe.HasBinary("xsel")` → `xsel`.
   d. None → return null, which the console app surfaces as exit 127 with an install hint.

### Console App (`clip`)

1. Parse args (`ArgParser`).
2. Resolve mode via `ResolveMode`.
3. If `--describe`, emit JSON and exit 0.
4. If `--help` / `--version`, emit and exit 0.
5. Create backend via factory; exit 127 with install hint if none.
6. Execute the mode:
   - `Copy`: read stdin as UTF-8, call `backend.CopyText`.
   - `Paste`: call `backend.PasteText`, strip trailing newline unless `--raw`, write to stdout as UTF-8.
   - `Clear`: call `backend.Clear`.
7. Map any thrown exception to an exit code and stderr message.

No threading, no async required — stdin/stdout are small by assumption (clipboard-sized).

---

## Data Flow

**Copy:**

```
stdin bytes → UTF-8 decode → string → backend.CopyText → platform clipboard
```

Invalid UTF-8 input fails fast with exit 125 and a clear message. We do not attempt to guess encoding.

**Paste:**

```
platform clipboard → backend.PasteText → string → optional trailing-newline strip → UTF-8 encode → stdout
```

Empty clipboard returns an empty string, which becomes empty stdout and exit 0 (matches `pbpaste`). No error.

**Clear:**

```
backend.Clear → platform clipboard emptied
```

---

## Error Handling

| Condition | Exit | Stderr |
|---|---|---|
| Success | 0 | — |
| Empty clipboard on paste | 0 | — (empty stdout) |
| Invalid UTF-8 in stdin during copy | 125 | `clip: invalid UTF-8 in input` |
| Unknown flag / malformed args / conflicting modes | 125 | usage hint |
| No Linux clipboard helper found | 127 | `clip: no clipboard helper found — install wl-clipboard, xclip, or xsel` |
| Windows `OpenClipboard` failed after retries | 126 | `clip: clipboard busy (another process holds it)` |
| Helper binary non-zero exit (shell-out) | 126 | helper's own stderr, passed through, prefixed `clip: ` |
| Internal / unexpected error | 126 | `clip: <message>` |

Exit-code convention matches the rest of the suite (125/126/127 for the tool's own errors; success is 0).

---

## Testing

Test classes mirror their subjects.

- **`ArgParserTests`** — table-driven matrix for every flag combination, including:
  - Positive: `clip`, `-c`, `-p`, `--clear`, `-r`, `--primary`, combined short flags.
  - Negative: `-c -p`, `--clear -c`, `--clear < stdin-redirected`, unknown flag, unknown positional.

- **`ResolveModeTests`** — pure function tests for every `(stdinRedirected, flags) → mode` combination.

- **`NewlineStripTests`** — table-driven:
  - `"foo"` → `"foo"`
  - `"foo\n"` → `"foo"`
  - `"foo\r\n"` → `"foo"`
  - `"foo\n\n"` → `"foo\n"` (only one stripped)
  - `"foo\r\n\r\n"` → `"foo\r\n"`
  - `--raw` preserves all.

- **`ShellOutClipboardBackendTests`** — inject a fake `IProcessRunner`; assert for each helper set:
  - Copy streams stdin in unchanged.
  - Paste returns captured stdout unchanged.
  - Clear uses the right command (empty-stdin copy for `pb`/`xclip`, explicit `--clear` for `wl-copy`/`xsel`).
  - `--primary` modifies arg vectors correctly for each helper.
  - Non-zero exit surfaces helper stderr.

- **`ClipboardBackendFactoryTests`** — inject a fake `IPlatformProbe`; assert:
  - Windows → `WindowsClipboardBackend`.
  - macOS → shell-out with pb helper set.
  - Linux + Wayland + `wl-copy` present → wl-clipboard.
  - Linux + no Wayland + `xclip` → xclip.
  - Linux + no Wayland + no xclip + `xsel` → xsel.
  - Linux + nothing available → null (surfaced as 127).

- **`WindowsClipboardBackendTests`** — integration tests guarded by `[SkipOnPlatform(OSPlatform.Linux, OSPlatform.OSX)]`:
  - Copy-then-paste round-trip.
  - Clear-then-paste returns empty.
  - Retry path covered via a timed second-opener fake (best-effort).

- **`FormattingTests`** — usage message, `--describe` JSON shape snapshot, error-prefix consistency.

All tests xUnit, AOT-compatible (no reflection, no `Moq`-style dynamic proxies — we use hand-written fakes).

---

## Distribution

Mirrors every other Winix tool:

- **Scoop manifest:** `bucket/clip.json`.
- **Suite bundle:** add `clip` to `bucket/winix.json`'s `bin` array.
- **Release pipeline:** add `clip` to the manifest-generation and combined-zip steps in `.github/workflows/release.yml`, and to the winget manifest step for stable releases.
- **NuGet global tool:** package ID `Winix.Clip` (already listed in CLAUDE.md).
- **Docs:** `src/clip/README.md` (full README, suite pattern), `docs/ai/clip.md` (AI agent guide), add to `llms.txt`, update CLAUDE.md project layout.
- **Man page:** `src/clip/clip.1` (groff), rendered by our own `man` tool.

---

## Windows Naming Note

Windows ships `clip.exe` in `System32`. For our `clip.exe` to take precedence, it must appear earlier in `PATH`. In practice:

- **Scoop** installs to `~\scoop\shims`, which is typically ahead of `System32` when Scoop is installed correctly. No user action required.
- **winget** / manual / direct-download users need to ensure their install directory is earlier in `PATH`. Documented in `src/clip/README.md`.
- **Old scripts that use `clip.exe`** continue to work unchanged — ours is a strict superset for write-mode stdin piping. The only newly-visible behaviour (paste, clear) requires an explicit invocation, so old scripts cannot accidentally encounter different semantics.

Validating `PATH` ordering post-install is out of scope for `clip` itself; a future `winix doctor` or `winix install` enhancement could surface this as a health check.

---

## v2 Scope (Deferred — Not In This Design)

The following are explicitly out of scope for v1 and should land as a single coherent feature later:

- `--format text|html|rtf` on copy and paste — drives content-type selection on the clipboard (`CF_HTML`/`CF_RTF` on Windows with the quirky HTML header format, `text/html`/`text/rtf` mime on Linux, NSPasteboard types on macOS).
- `--image` — read/write PNG or other image formats.
- `--files` — read/write file-reference clipboard items.
- `--list-formats` — enumerate the content types currently on the clipboard.
- **Native NSPasteboard backend for macOS** — this is where macOS moves from "thin wrapper" to genuine gap-fill, because `pbcopy`/`pbpaste` cannot handle non-text types.
- **Native X11/Wayland backend for Linux** — only if a strong reason emerges (self-contained binaries without `xclip` dependency). Currently: not worth the effort.

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|---|---|
| Rich-type support (images/files/HTML/RTF) | Bundled as v2; macOS needs NSPasteboard native work regardless, so splitting rich-type features across versions would duplicate effort. |
| `--list-formats` | Couples tightly with rich-type support; belongs with v2. |
| Native Linux backend | Weeks of X11 ICCCM / Wayland protocol work for marginal benefit over shelling out. Door left open. |
| Clipboard history | Explicitly not a goal — `clip` is a bridge, not a clipboard manager. |
| Windows PATH-ordering validation | Out of scope for `clip`; candidate for a future `winix doctor` health-check. |
