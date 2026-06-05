# Windows Glob Expansion — Design

**Date:** 2026-06-05
**Status:** Approved (brainstorm complete)
**Companion ADR:** `2026-06-05-glob-expansion-adr.md`
**Target branch:** `feature/glob-expansion` → `release/v0.4.0`

## Problem

On Windows, `cmd.exe` and PowerShell do not expand glob patterns (`*`, `?`) before
launching native executables — the literal pattern arrives in argv. Unix shells and
Git Bash do expand. So `digest *.dll` works on Linux/macOS/Git Bash but fails with
"not found: *.dll" in cmd/pwsh — a confusing inconsistency for a cross-platform suite.

Discovered 2026-04-19 during manual testing of `digest`. Deferred to a suite-wide
fix in `Yort.ShellKit` so glob semantics stay consistent across tools (per the
suite's CommandLineParser-owns-CLI-behaviour convention).

**Prep already landed (2026-05-31, commits `c26344f` + `ccd5b50`):** `GlobMatcher`
moved from `Winix.FileWalk` into `Yort.ShellKit` — the matcher half is done, the
circular-dependency blocker is gone, and the 8.3 short-name trap is structurally
neutralised (pure in-process anchored regex; nothing delegates pattern matching to
the OS).

## Goals

- `digest *.dll`, `squeeze logs\*.log`, `trash *.tmp`, `treex */src` etc. work in
  cmd.exe and PowerShell the same way they work in bash.
- One implementation in ShellKit; per-tool adoption is a one-line opt-in.
- Non-adopting tools are byte-identical (no forced suite-wide retest).
- Every boundary of the supported surface either behaves exactly as today or fails
  loudly with a documented message. No silent half-working cases.

## Non-goals

- Expanding option/list-option **values** (`files --include "*.cs"` is a
  tool-interpreted pattern, not a path to expand).
- Expanding command-mode `Command` args (`wargs`, `retry`, `timeit`, `peep`, `nc` —
  the child process's args are not ours).
- `**` recursive globbing (loud error in v1; revisitable).
- `[...]` character classes (permanently excluded — see Safety model).
- Unix behaviour changes of any kind (feature is a no-op off Windows).

## Safety model (why partial support is safe here)

Two Windows filesystem facts anchor the design:

1. **`*` and `?` are illegal in Windows file and directory names.** An argument
   containing them *cannot* be a real path, so today it is guaranteed to fail
   ("not found") in every adopting tool. Expansion therefore has a **monotonicity
   property**: only guaranteed-failing inputs change behaviour. A matched pattern
   starts working; an unmatched pattern passes through literally and produces the
   same error as today. No currently-working invocation can regress.

2. **`[` and `]` are legal filename characters**, and real files like
   `report[1].txt` are common (browser download-dedup naming). Treating `[...]` as
   a character class would misread genuine paths as patterns — a false-positive
   class with no safe detection. Excluding `[...]` is the correct permanent design,
   not a v1 economy.

## Decisions (summary — rationale in the ADR)

| # | Decision |
|---|----------|
| 1 | Expansion is parser-integrated: fluent `ExpandGlobPositionals()` opt-in on `CommandLineParser`; `Parse()` does the work; `ParseResult.Positionals` arrives pre-expanded |
| 2 | Syntax surface: `*` and `?` in any path segment; `[...]` always literal; `**` → loud usage error |
| 3 | Quoting honoured: args that were quoted on the raw command line are not expanded (cmd-accurate; pwsh caveat documented). Tokenizer-alignment mismatch fails open (expand everything) |
| 4 | No match → literal passthrough (bash nullglob-off) |
| 5 | bash/Git-Bash parity for semantics: dotfile rule, hidden/system attributes ignored, files+dirs both match |
| 6 | Windows-only gate (`OperatingSystem.IsWindows()`), injectable for tests |
| 7 | v1 adopters: digest, squeeze, trash, less, treex, files |

## Architecture

Three units in `Yort.ShellKit` + a thin parser integration:

### 1. `RawCommandLineTokenizer` (pure, ~150 LOC)

- Input: raw command-line string (`Environment.CommandLine` at runtime — no P/Invoke).
- Output: ordered tokens, each carrying `Text` (quotes stripped, escapes resolved)
  and `WasQuoted`.
- Replicates the CRT / `CommandLineToArgvW` rules so token boundaries align 1:1
  with the `string[] args` .NET delivers to `Main`: backslash-run-before-quote
  counting, `""` escaping inside quoted regions, and argv[0]'s simpler rule
  (no backslash escaping; terminated by closing quote or whitespace).
- Prior art / reference spec: the Rust `wild` crate (used by ripgrep for this exact
  problem) and Microsoft's documented parsing rules.
- Entirely pure → exhaustively unit-testable on all platforms.

### 2. `GlobArgExpander` (engine, ~200 LOC)

- Input: one argument string. Output: sorted matches, or no-match, or
  unsupported-form (`**`).
- Splits the pattern at `/`/`\` separators. Walks the literal prefix (handles
  relative paths, `..`, drive roots `C:\`, UNC `\\server\share`). For each
  wildcard-bearing segment, enumerates **that single directory level**
  (`Directory.EnumerateFileSystemEntries(dir)` — never
  `Directory.GetFiles(dir, pattern)`, which would resurrect the 8.3 short-name
  trap) and filters names via `GlobMatcher` (case-insensitive).
- Filesystem access goes through an injectable enumeration seam (internal
  `Func`/interface) → engine logic is testable cross-platform against a fake FS.

### 3. `CommandLineParser` integration (~10–15 LOC in `Parse()`)

```csharp
var parser = new CommandLineParser("digest", Version)
    .ExpandGlobPositionals()   // all positionals are paths (v1 adopters)
    ...
// Future subcommand tools: .ExpandGlobPositionals(skipFirst: 1)
```

After arg classification, when opted in **and** on Windows:

- For each positional containing `*` or `?` (including positionals after `--`;
  never `Command` args): look up its original argv index, check `WasQuoted` via
  the tokenizer, and if unquoted, splice its expansion in place (or pass the
  literal through on no-match).
- A `**`-bearing positional adds a parse error through the existing
  `Errors`/`WriteErrors` machinery — correct text and `--json` formatting and the
  tool's usage-error exit code, for free.
- If the tokenizer's token count does not align with `args.Length` (exotic
  command line or tokenizer gap): **fail open — expand everything.** A quoted glob
  falling back to expansion is benign (worst case it matches, which is usually the
  intent; no-match passes the literal anyway). Silently disabling expansion would
  be an invisible feature outage — the worse failure.
- Opted-in parsers automatically add a standard wildcard line to `--help` and a
  `glob_expansion` field to `--describe`, so the contract is machine-discoverable
  with no per-tool drift.

**Data flow:** `argv` → classify (existing) → [Windows + opted-in] quoting check →
expand unquoted glob positionals in place (each pattern's matches sorted, spliced
at its argv position — bash behaviour) → `ParseResult.Positionals`.

## Expansion semantics

| Rule | Behaviour | Rationale |
|---|---|---|
| Metacharacters | `*`, `?` only; any segment | Illegal in Windows names → unambiguous detection |
| `[...]` | Always literal | Legal filename chars (`report[1].txt`) |
| `**` | Usage error via `Errors`: "recursive '**' is not supported in argument expansion; use the tool's recursive options or 'files'" | Loud signpost; agent-friendly |
| No match | Literal token passes through unchanged | bash nullglob-off; preserves today's "not found" |
| Match set | Files **and** directories | bash parity; tools already validate what they accept |
| Trailing separator (`*/`, `bin\*\`) | Directories only; separator preserved on output | bash parity |
| Leading-dot entries | `*`/`?` at segment start do not match a leading `.`; pattern segment must start with `.` to match | bash/Git Bash parity — same script, same result |
| Hidden/system attributes | Ignored — attribute'd files match normally | Attributes ≠ dotfiles; matches Git Bash on Windows. Documented |
| Case sensitivity | Case-insensitive (`GlobMatcher(caseInsensitive: true)`) | Windows filesystem semantics |
| Sort order | Per-pattern `StringComparer.OrdinalIgnoreCase`; spliced at the pattern's argv position | Deterministic, locale-independent |
| Relative patterns | Resolved against current directory; `..\*.cs` works | Literal-prefix walk handles it |
| Rooted / UNC | `C:\x\*.dll`, `\\server\share\*.log` work | Literal prefix is generic |
| `-` (stdin sentinel) | Untouched (no metachars) | Trivially safe |
| Args after `--` | Still expanded | bash expands after `--` too; quoting is the suppression mechanism, `--` is flag termination |
| Command-mode `Command` | Never expanded | Child's args are not ours |
| Enumeration errors (access denied, invalid path) | Swallowed → that directory contributes no matches → typically literal passthrough → tool's normal error | bash behaviour; diagnostic-weaker-than-production rule |
| Non-Windows | Entire feature is a no-op | Unix shell already expanded; never double-expand |
| Empty-result safety | Impossible by construction — no-match returns the literal, so `Positionals` can never silently shrink | Tools' "no files given" validation keeps working |

### Deliberate asymmetry vs cmd

cmd's `dir *` hides attribute-hidden files; our `*` matches them (so `digest *`
includes `desktop.ini`-class files). Chosen deliberately: the suite's promise is
"same behaviour as the Unix side", and attribute filtering would be a silent
data-dependent divergence. Documented in README/docs-ai sections.

## Known limitations (documented, accepted)

1. **PowerShell re-quotes.** Quoting detection reads the raw command line the
   *shell built*, not what the user typed. cmd.exe passes the typed line through
   verbatim → quote suppression works. PowerShell parses args and re-builds the
   command line, quoting only tokens that need it → `digest '*.dll'` reaches us
   unquoted and expands. pwsh users get literal passthrough only via `--%` (or by
   the no-match fallthrough — and since `*` files cannot exist on Windows, the
   practical loss is error-message shape, never data). Docs say: "quoting
   suppresses expansion in cmd and shells that preserve quotes; PowerShell
   normalises quoting away."
2. **Git Bash double-expansion of quoted literals.** Git Bash expands globs itself
   → we see concrete paths → idempotent (no metachars, untouched). But a
   bash-*quoted* glob (`digest "*.dll"` in Git Bash) is passed to the process
   unquoted-if-simple, and our Windows expansion may re-expand it. Same
   nil-practical-impact argument as above. Documented caveat.
3. **`**` not supported** — loud error in v1; can be added later behind the same
   opt-in if demand shows up.
4. **Drive-relative patterns (`C:*.txt`) are not supported.** The segment model
   cannot express "current directory of drive C:"; such a pattern never matches
   and passes through literally — same failure the user gets today. (Adversarial
   review F1.)
5. **Access-denied on a user-typed literal prefix reads as "not found".** An
   unlistable `protected\` in `protected\*.txt` yields literal passthrough and
   the tool's normal not-found error, which is misleading about *why*. Deliberate:
   bash behaves identically (`cat protected/*.txt` → "No such file or directory"),
   and Decision 5 anchors bash parity. Revisit if it generates support traffic.
   (Adversarial review F2.)
6. **No expansion cap, no expansion trace.** A directory level is fully enumerated
   regardless of size (bash has no cap either), and no artifact records whether a
   given invocation expanded, failed open, or passed a literal through —
   `--describe`'s `glob_expansion` field is the only machine-discoverable surface
   in v1. (Adversarial review F4, F7.)
7. **`.`/`..` segments after a wildcard never match.** `*\..\x.txt` collapses to
   no-match → literal passthrough (enumeration returns no dot entries; literal
   segments after a wildcard match by enumerated-name equality). bash would expand
   it. Monotonic — the literal fails exactly as it does today. Pinned by
   `DotDotAfterWildcard_IsNoMatch_DocumentedLimitation`. (Tasks 4–5 quality review.)

## Testing strategy

Four layers, each mapped to what it can actually prove:

1. **Tokenizer unit tests (pure, all platforms).** Vectors from the documented CRT
   rules (backslash-run-before-quote, `""` escaping, argv[0] rule) plus the `wild`
   crate's test corpus as a cross-check. Then one **self-referential integration
   test** (Windows, `SkippableFact` + redundant platform guard per suite
   convention): spawn the test host with crafted command lines and assert the
   tokenizer's unquoted token text exactly equals the `args[]` .NET delivered —
   pins "our rules == the runtime's rules", the alignment guarantee the quoting
   feature rests on.
2. **Engine unit tests (fake FS seam, all platforms).** Segment walking, dotfile
   rule, trailing-separator dirs-only, sort/splice order, no-match passthrough,
   `**` rejection, UNC/rooted prefixes, denied-directory swallow. Run on Linux CI
   too — the seam removes real I/O.
3. **Parser integration tests (all platforms via seams).** Opt-in vs not,
   Windows-gate injection, quoted-vs-unquoted suppression, `--`/`-`/command-mode
   exclusions, fail-open on alignment mismatch, `**` error through text and
   `--json` paths. Existing parser tests pass untouched — locking the
   "non-adopters byte-identical" claim with a test.
4. **Per-tool smokes (irreducible manual layer).** In-process tests cannot
   reproduce "cmd/pwsh didn't expand" — they see whatever argv we fabricate. Each
   adopter gets real `cmd.exe` and `pwsh` smoke invocations (match, no-match,
   quoted-in-cmd, `**` error) scripted into the existing native-capability smoke
   pattern, plus a Git Bash idempotency spot-check. **Ship gate per adopter, not a
   nice-to-have.**

## Adoption & documentation (per tool: digest, squeeze, trash, less, treex, files)

- One-line `.ExpandGlobPositionals()` opt-in.
- README + man page: short standard **Wildcards on Windows** section (shared
  wording, adapted per tool).
- `docs/ai/{tool}.md`: support matrix (`*`/`?` yes, `[...]` literal, `**` error,
  quoting caveat incl. pwsh re-quoting).
- ShellKit emits the `--help` line and `--describe` `glob_expansion` field
  automatically from the opt-in declaration.
- Side effect: closes open trash finding #6 — the `trash *.log` help example
  becomes true on Windows and cross-references the new section.

## Rollout

ShellKit core first (tokenizer + engine + parser integration + tests), then
adopters heaviest-first (digest → squeeze → trash → less → treex → files), each
with its smokes and doc updates. All on `feature/glob-expansion`, merged `--no-ff`
into `release/v0.4.0` (mkauth precedent).

## Cost estimate

~2 days total: tokenizer + tests ~½ day; engine + tests ~½–1 day; parser
integration + tests ~½ day; per-tool adoption ~30–60 min each including smokes and
docs. (Pre-implementation estimate — re-validate at planning.)
