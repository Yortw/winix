# Winix — Tool Backlog

**Status:** Ideas captured, NOT yet brainstormed or designed. Nothing here is approved for
implementation. Each entry needs its own brainstorm → design → ADR → plan before any code,
per the suite conventions in `CLAUDE.md`.

This file is the living "what might we build next" list. The original frozen brainstorm
(`docs/plans/2026-03-28-winix-design-notes.md`) covered the first wave of tools and is history;
this is where new candidate *classes* and tools accumulate.

---

## Context: the category gap

The suite (~26 tools as of 2026-05) has filled the "Windows has no such command" holes
(`peep`/watch, `wargs`/xargs, `files`/find, `nc`, `man`, `less`) and added generators, codecs,
and secret tooling. It has **not** entered the text-stream / record-processing category at all —
there is no `grep`/`sort`/`cut`/`uniq`/`jq`/`join` equivalent in the suite.

Analysing the gap through the LINQ standard query operators is clarifying: the shell already
nails the **sequence** operators (`Select`/`Where`/`Take`/`OrderBy`/`Zip` → `awk`/`grep`/`head`/
`sort`/`paste`) but leaves the **relational / set** operators (`Intersect`/`Except`/`Union`/
`Join`/`GroupBy`/`DistinctBy`) half-finished — they exist only as sort-dependent, whole-line
footguns (`comm`, `join`) or as add-on tools (`datamash`, Miller) that aren't native on Windows.

Three candidate classes fall out, below.

---

## Class 1 — Relational family (LINQ/SQL operators as stream filters)

**Thesis:** field-aware, hash-based, **sort-free** relational operators over line/TSV/CSV/JSON
streams. The operators coreutils does badly or not at all, done as first-class composable filters.
This is greenfield in the suite — no existing tool overlaps.

**Build-vs-install note (must resolve during brainstorm):** GNU `datamash` and Miller (`mlr`)
already fill much of this on Linux and can be dropped into Git Bash. The winix case rests on a
concrete reason — single-exe zero-install deployment onto locked-down Windows boxes, Windows-path
awareness, suite consistency (ShellKit `--describe`/`--json`/exit codes), identical behaviour
everywhere. If that reason isn't real for a given operator, prefer recommending the existing tool.

Candidate tools (names provisional):

- **`setop`** — `intersect` / `except` / `union` / `symdiff` of two (or N) streams on a chosen
  key/field, hash-based, order-preserving, no pre-sort required. Fixes the `comm` footguns
  (must be sorted; whole-line only).
  - `setop intersect --key 2 a.tsv b.tsv`
  - `setop except current.txt allowed.txt`
- **`join`** — relational join (inner/left/outer) on a key field, hash-based, no pre-sort.
  Fixes the `join`-requires-sorted-input footgun. (Name collides with POSIX `join` — TBD.)
  - `join --left orders.tsv customers.tsv --on 3=1`
- **`groupby`** — `GroupBy` + aggregation: count/sum/min/max/avg/first/last/collect per group key.
  The single biggest raw-coreutils gap (`sort | uniq -c` only counts whole lines).
  - `groupby --key 1 --sum 3 --count sales.tsv`
- **`distinct`** — order-preserving `Distinct` / `DistinctBy` on a chosen key (the
  `awk '!seen[$k]++'` idiom as a real tool, plus key selection).
  - `distinct --key 2 access.log`

**Open questions for brainstorm:** record model (lines vs TSV vs CSV vs JSON — pick one universal
or support several?); how field/key selection works (index, name, regex); memory model for the
hash side of joins/sets on large inputs; whether these are one multi-subcommand tool or several
binaries; relationship to a possible future `jq`-like projection tool.

---

## Class 2 — Adapter family (Windows sources → universal line interface)

**Thesis:** the `gron` / `jc` / `htmlq` pattern, applied to Windows-shaped data. Take a structured-
but-not-line-oriented Windows source and emit clean lines / TSV / JSON so the *existing* text
pipeline (and Class 1's relational tools) can operate on it. Genuinely empty quadrant — no
`datamash`/Miller equivalent exists because PowerShell occupied this niche with *objects*, leaving
text-stream composition thin on Windows.

These are **producers of rows**; Class 1 are **consumers of rows** — two halves of one idea
("relational ops over Windows data streams"). e.g. `eventlog | groupby`, or
`setop except <(reg-flat HKLM\...\A) <(reg-flat HKLM\...\B)`.

Candidate adapters (names provisional):

- **Registry → flat assignment lines** (gron-style, reversible): `key = value` lines that
  `grep`/`sort`/`setop`/`diff` can chew on. Registry-as-a-diffable-stream.
- **Event Log → records** (line/TSV/JSON): query Windows Event Log into greppable rows.
- **WMI/CIM → records**: services, processes, scheduled tasks, disks as queryable row streams.
- **ACLs / permissions → records**: flatten NTFS ACLs to rows for diff/audit.
- (general) a **`gron`-for-JSON** primitive — flatten arbitrary JSON to greppable assignment
  lines and back. Cross-platform, not Windows-specific, but the same adapter shape.

**Open questions for brainstorm:** which source first (registry is the most compelling demo);
reversibility (gron's `-u` round-trip) yes/no per adapter; how much each overlaps PowerShell
(only worth building where the text-pipeline integration genuinely beats `Get-*  | ...`).

---

## Class 3 — Domain codec (EPC / GS1)

**Thesis:** a `jq`-for-tags codec primitive. Orthogonal to Classes 1–2 (not stream-relational —
a pure stdin→stdout transform/validate), but highest personal-itch given the RFID/retail domain.
Codec primitives (`base64`, `tr`, `digest`, `url`) are the most composable class there is, and the
suite already has several (`ids`, `qr`, `url`, `digest`) so it fits the established shape.

Candidate tool (name provisional):

- **`epc`** — decode / encode / validate EPC tag identifiers: SGTIN-96 hex ⇄ EPC tag URI ⇄
  GTIN+serial, plus validation. Possibly broader GS1 element-string parsing.
  - `epc decode 3034F4257BF400C000000001`
  - `epc encode --gtin 00012345678905 --serial 1`
  - `epc validate < tags.txt`

**Verify during brainstorm:** whether a clean *composable CLI* (vs a library) for EPC already
exists; which EPC encodings to cover first (SGTIN-96 is the retail-apparel common case); whether
this belongs in winix or as a standalone domain tool.

---

## Class 4 — Stream-plumbing / routing primitives

**Thesis:** the structural duals to map/filter. Where the relational family (Class 1) and ordinary
transforms change record *content*, these manipulate the *topology and timing* of streams —
broadcast, partition, merge, gate, buffer, retime, instrument. Tiny single-purpose primitives,
"basically a programming primitive" in shell form. Most exist only on Linux (moreutils et al) with
essentially no Windows presence; two (`switch`, `merge`) appear to lack a tidy standalone anywhere.

**Build-vs-install note:** several are moreutils ports — the winix value is cross-platform identical
behaviour + Windows presence + suite consistency (`--describe`/`--json`/exit codes), NOT novelty.
Unlike the relational engine (Class 1), **no mature single-binary occupies these cells on Windows**,
so the consistency premium is far easier to justify here. `switch` and `merge` look genuinely
under-served everywhere and are the strongest "new primitive" candidates. Per-tool cost/benefit
check still applies.

### The topology lattice (how the family fits, and where the gaps are)

| Stream shape | Primitive | Tidy standalone? |
|---|---|---|
| 1→N broadcast to files | `tee` | yes (built-in elsewhere) |
| 1→N broadcast to commands | `pee` | Linux only (moreutils) |
| 1→1-of-N partition by predicate | **`switch`** | **no — awk only** |
| N→1 live merge / interleave | **`merge` / `fanin`** | partial (`cat` sequential; `tail -f` multi) |
| 1→0-or-1 gate by presence | `ifne` | Linux only (moreutils) |
| 1→1 buffer-then-write | `sponge` | Linux only (moreutils) |
| 1→1 rate-limit | `throttle` | no tidy Windows tool |
| 1→1 instrument (meter) | `pv` | Linux-centric; low Windows value |
| 1→1 tty-emulate | `pty` / `faketty` | `unbuffer` (expect); ConPTY on Windows |
| pipeline, producer exit code | `mispipe` | bash `pipefail` only; gap on Windows |
| run, output only on failure | `chronic` | Linux only (moreutils) |

### Candidate tools (names provisional)

Genuinely new primitives (under-served everywhere — strongest case):

- **`switch`** — partition a stream: route each record to one of N commands/files by predicate
  (first-match), with a `--default`. The partition-dual of `pee`'s broadcast. Today this is
  hand-rolled `awk '/A/{print > "a"} /B/{print > "b"}'` — no discoverable standalone verb.
  - `cat app.log | switch --case /ERROR/ 'tee err.log' --case /WARN/ 'tee warn.log' --default 'tee other.log'`
  - Name note: prefer `switch` over `route` — `route.exe` (routing table) collides on Windows.
- **`merge` / `fanin`** — the N→1 dual: interleave several *live* sources into one stream, line-atomic,
  in arrival order. `cat` is sequential; `tail -f a b` is a partial special case. Medium-sized, not
  tiny (line-atomicity across concurrent producers is the hard part).

Windows gaps with a concrete mechanism:

- **`throttle`** — rate-limit a pipe (`throttle 2m` = cap at 2 MB/s). Pure pass-through, touches only
  timing. Real use: DB dumps / network-share copies without saturating disk or link.
- **`pty` / `faketty`** — run a command as if attached to a terminal so it keeps colour + line
  buffering when piped (`pty mytool | less -R`). Implementable natively via **ConPTY** on Windows
  (not faked). Tiny in concept, fiddly in ConPTY plumbing — needs a research spike before committing.

moreutils-class ports (value = cross-platform consistency, not novelty):

- **`sponge`** — soak all stdin, *then* write the file; enables in-place pipes (`sort f | sponge f`)
  that `> f` truncates. Should write atomically (temp + rename) and spill to a temp file past a size
  threshold rather than buffering multi-GB in RAM.
- **`pee`** — `tee` to *commands* instead of files; every child gets a full copy of stdin in one pass.
  Must swallow per-child broken-pipe failures (early child exit) without killing the whole `pee`.
- **`ifne`** — run a command only if stdin is non-empty (`-n` = only if empty). The `if`-gate primitive.
- **`mispipe`** — run `a | b` but return `a`'s exit code, not `b`'s. bash has `pipefail`/`PIPESTATUS`;
  cmd.exe and PowerShell have no equivalent, so this is *more* valuable on Windows than Linux.
- **`ts`** — prepend a timestamp to each line of stdin (`cmd | ts`). Relative/elapsed mode too (`ts -s`).

Adjacent (command-runner family, not dead-centre plumbing — file beside `retry`/`timeit`/`peep`):

- **`chronic`** — run a command; print its output only if it fails. Cron/CI noise killer.

**Open questions for brainstorm:** one multi-subcommand binary vs several tiny binaries (these are
*very* small — packaging overhead may dominate); broken-pipe / partial-write handling model on Windows
(shared across `pee`/`switch`/`sponge`); whether `pty` is cleanly implementable via ConPTY (spike);
naming collisions (`switch` ok, `route` no, `merge` is a common word but probably fine); how predicates
are expressed in `switch` (regex only, or also field/value and exit-code-of-a-test-command).

---

## Not-doing / parked

- Re-porting coreutils sequence tools (`sort`, `tac`, `cut`, `head`) — already available in Git
  Bash; no winix-specific reason unless a Windows-path-awareness gap is demonstrated.
