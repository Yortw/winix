# ids — Identifier Generator

**Date:** 2026-04-17
**Status:** Proposed
**Project:** Winix (`D:\projects\winix`)

---

## Overview

`ids` is a cross-platform CLI tool that generates identifiers: **UUID v4**, **UUID v7** (default), **ULID**, and **NanoID**. Single AOT-compiled binary, no runtime dependency, uniform flag surface across all ID types.

**Why it's needed:**

- **Linux `uuidgen`** produces v1/v4 only — no v7, ULID, or NanoID.
- **macOS `uuidgen`** produces v4 only.
- **Windows** has `[guid]::NewGuid()` in PowerShell — v4 only, 300+ ms startup, not always on PATH.
- **Generating modern ID types** (UUID v7, ULID, NanoID) on any platform today requires reaching for a language runtime (`npx uuid`, `python -c 'import ulid; ...'`) — which is exactly the pattern Winix exists to kill. Single native binary with ~10 ms startup beats all of them.

**Primary use cases:**

- Quick one-off: `ids` → a UUID v7.
- Batch: `ids --count 100 > ids.txt`.
- URL-safe slugs: `ids --type nanoid --length 12` → `4f2Xp7qW9aBr`.
- Sortable database keys: `ids --type uuid7` (default).
- Piped composition: `ids | clip`, `ids --type nanoid --count 5 | xargs -I{} curl http://host/{}`.

**Positioning:**

- Against `uuidgen`: **modern types** (v7, ULID, NanoID) that `uuidgen` can't do.
- Against `npx uuid` / `python -c`: **native binary startup** (≈10 ms vs 300–500 ms).
- Consistent flag surface across all four ID types — no remembering per-type syntax.

**Platform:** Cross-platform (Windows, Linux, macOS). No platform-specific code — pure random + time.

---

## Project Structure

```
src/Winix.Codec/             — NEW shared library
  Base32Crockford.cs         — Encode(byte[]) / Decode(string) — case-insensitive, no padding
  SecureRandom.cs            — thin ISecureRandom wrapper over RandomNumberGenerator.Fill
  Winix.Codec.csproj         — net10.0, trim + AOT, no deps

src/Winix.Ids/               — class library
  IdType.cs                  — enum: Uuid4, Uuid7, Ulid, Nanoid
  UuidFormat.cs              — enum: Default, Hex, Braces, Urn
  NanoidAlphabet.cs          — enum + ToChars(): UrlSafe, Alphanum, Hex, Lower, Upper
  IdsOptions.cs              — parsed args (Type, Count, Length, Alphabet, Format, Uppercase, Json, …)
  IIdGenerator.cs            — interface: string Generate(IdsOptions)
  Uuid4Generator.cs
  Uuid7Generator.cs
  UlidGenerator.cs           — monotonic, injectable ISystemClock + ISecureRandom
  NanoidGenerator.cs         — rejection-sampling, injectable ISecureRandom
  IdGeneratorFactory.cs
  ArgParser.cs               — parse + Q5 flag/type compatibility validation
  Formatting.cs              — UUID Default/Hex/Braces/Urn + case
  DescribeJson.cs
  ISystemClock.cs / SystemClock.cs
  Winix.Ids.csproj           — net10.0, trim + AOT, refs Winix.Codec + Yort.ShellKit

src/ids/                     — thin console app
  Program.cs                 — argv → parse → factory → loop → stdout
  ids.csproj                 — publishes AOT
  README.md
  man/man1/ids.1             — groff

tests/Winix.Codec.Tests/     — xUnit
tests/Winix.Ids.Tests/       — xUnit

bucket/ids.json              — scoop manifest
docs/ai/ids.md               — AI agent guide
```

Standard Winix conventions: library does all work, console app is thin. `Winix.Codec` follows the `Winix.FileWalk` precedent — shared library created alongside its first consumer (`ids`), reused later by `digest`.

Tool binary name is **`ids`** (plural) rather than `id`, to avoid shadowing the POSIX `id` coreutil that prints user UID/GID on Linux/macOS.

---

## CLI Interface

```
ids                                       # one UUID v7 (default)
ids --type uuid4                          # random UUID v4
ids --type ulid                           # ULID, 26 Crockford-base32 chars, uppercase
ids --type nanoid                         # NanoID, 21 url-safe chars
ids --type nanoid --length 12             # NanoID, 12 chars
ids --type nanoid --alphabet hex          # 21 random hex chars
ids --count 10                            # ten UUID v7s, one per line
ids --type uuid7 --format hex             # 32 hex chars, no hyphens
ids --type uuid7 --format braces --uppercase   # {018E7F6F-...-4A5D}
ids --type uuid7 --format urn             # urn:uuid:018e7f6f-...
ids --json --count 3                      # [{"id":"...","type":"uuid7"}, …]
ids --describe / --help / --version
```

### Flags

| Flag | Short | Default | Applies to | Values |
|---|---|---|---|---|
| `--type T` | `-t T` | `uuid7` | all | `uuid4`, `uuid7`, `ulid`, `nanoid` |
| `--count N` | `-n N` | `1` | all | `N ≥ 1` (exit 125 if ≤ 0) |
| `--length N` | `-l N` | `21` | `nanoid` only | `N ≥ 1` |
| `--alphabet A` | | `url-safe` | `nanoid` only | `url-safe` (A–Za–z0–9_-), `alphanum` (A–Za–z0–9), `hex` (0–9a–f), `lower` (a–z0–9), `upper` (A–Z0–9) |
| `--format F` | | `default` | `uuid4`, `uuid7` only | `default` (hyphenated), `hex` (32 chars no hyphens), `braces` (`{xxxxxxxx-…}`), `urn` (`urn:uuid:xxxxxxxx-…`) |
| `--uppercase` | `-u` | off | `uuid4`, `uuid7` only | orthogonal to `--format` |
| `--json` | | off | all | single JSON array to stdout |
| `--color` / `--no-color` | | auto | — | reserved; v1 output is plain. Accepted for suite consistency. |
| `--describe` | | — | — | AI discoverability JSON |
| `--help` | `-h` | — | — | |
| `--version` | `-v` | — | — | |

### Flag / Type Compatibility (error on mismatch)

| Combination | Behaviour |
|---|---|
| `--length` with `--type uuid4`/`uuid7`/`ulid` | exit 125: `ids: --length only applies to --type nanoid` |
| `--alphabet` with `--type uuid4`/`uuid7`/`ulid` | exit 125: `ids: --alphabet only applies to --type nanoid` |
| `--format` with `--type ulid`/`nanoid` | exit 125: `ids: --format only applies to --type uuid4 or uuid7` |
| `--uppercase` with `--type ulid` | exit 125: `ids: ULID output is already uppercase` |
| `--uppercase` with `--type nanoid` | exit 125: `ids: use --alphabet upper for uppercase NanoID` |
| `--count ≤ 0` or `--length ≤ 0` | exit 125 with specific message |
| Unknown `--type`/`--format`/`--alphabet` value | exit 125, lists valid values |

Rationale: unlike `clip --primary` on Windows (where silent-ignore supports portable cross-platform scripts), here the mismatch is a *conceptual* error in one invocation — the user has either typo'd or misremembered the flag's applicability. Strict rejection catches that. See ADR §5.

### Output

- **Plain mode (default):** one ID per line, UTF-8, trailing newline. Stream-composable: `ids --count 5 | while read id; …` works.
- **`--json` mode:** a single JSON array to stdout, followed by one trailing newline. Each element is `{"id":"<value>","type":"<type>"}` plus type-specific fields where relevant (e.g. NanoID elements include `"length"` and `"alphabet"`). For `--count 1`, still emits a one-element array (shape consistency beats ergonomic).

### `--describe`

Emits the standard Winix self-description JSON (purpose, flags, exit codes, examples). Matches every other tool in the suite.

---

## Architecture

### Class Library (`Winix.Codec`)

New shared library, first of its kind in the suite for encoding / crypto primitives. Matches the pattern set by `Yort.ShellKit` (env, formatting, parser) and `Winix.FileWalk` (directory walking).

**`Base32Crockford`:**

- `string Encode(ReadOnlySpan<byte>)` — produces Crockford base32 (alphabet `0123456789ABCDEFGHJKMNPQRSTVWXYZ`), uppercase, no padding.
- `byte[] Decode(string)` — case-insensitive, maps Crockford's ambiguity-avoidance substitutions (`I`→`1`, `L`→`1`, `O`→`0`) on input, rejects other invalid chars.
- No dependencies, pure compute.

**`SecureRandom`:**

- `ISecureRandom` interface with `void Fill(Span<byte> destination)`.
- Default implementation delegates to `RandomNumberGenerator.Fill` (BCL, AOT-safe, OS CSPRNG).
- Static `SecureRandom.Default` singleton.
- Interface exists so `Winix.Ids` tests can inject deterministic streams.

Future: `digest` will add hex, base64, more encoders plus `ConstantTimeEquals(ReadOnlySpan<byte>, ReadOnlySpan<byte>)` for HMAC verification. Those land with `digest`, not `ids`.

### Class Library (`Winix.Ids`)

**Core types:**

- `IdType` — enum: `Uuid4`, `Uuid7`, `Ulid`, `Nanoid`.
- `UuidFormat` — enum: `Default`, `Hex`, `Braces`, `Urn`.
- `NanoidAlphabet` — enum: `UrlSafe`, `Alphanum`, `Hex`, `Lower`, `Upper`. Exposes a `ToChars()` extension returning the `char[]` alphabet.
- `IdsOptions` — parsed flags: `Type`, `Count`, `Length`, `Alphabet`, `Format`, `Uppercase`, `Json`, plus the standard `Help`/`Version`/`Describe`/`ColorMode` fields.
- `IIdGenerator` — single-method interface:
  - `string Generate(IdsOptions opts)`

**Generators:**

- **`Uuid4Generator`:** `Formatting.FormatGuid(Guid.NewGuid(), opts.Format, opts.Uppercase)`. Uses OS CSPRNG via BCL. Stateless.

- **`Uuid7Generator`:** wraps `Guid.CreateVersion7()` with an application-level monotonicity guard. Despite what earlier drafts of this design claimed, **.NET 10's `Guid.CreateVersion7()` does NOT guarantee intra-ms monotonicity** — successive calls re-randomise the sub-timestamp bits and can produce descending values. Empirically verified: without a guard, the test `Generate_1000SequentialCalls_AreMonotonicallyOrdered` fails at iteration 1. The generator holds a `_last` field behind a lock; if a fresh candidate compares ≤ `_last` in canonical big-endian byte order (`Guid.TryWriteBytes(..., bigEndian: true)` + `SequenceCompareTo`), we increment `_last` by 1 as a 128-bit integer and return that. Allocation-free on the hot path.

- **`UlidGenerator`:** hand-rolled, ~45 lines, monotonic within the same ms.
  1. Read `ISystemClock.UnixMsNow()` → 48-bit big-endian timestamp (6 bytes).
  2. Under an internal lock:
     - If `ms == _lastMs`: increment the 80-bit random portion (`_lastRandom`) as a big-endian bigint. Overflow within a single ms is impossible (80 bits of headroom).
     - Else: `_lastMs = ms`; `_random.Fill(_lastRandom)`.
  3. Concatenate `ms (6 bytes)` + `_lastRandom (10 bytes)` → 16 bytes → `Base32Crockford.Encode` → 26 uppercase chars.

  **Monotonicity guarantee:** within the same millisecond, generation order equals sort order. UUID v7 gets the same guarantee via the application-level guard in `Uuid7Generator`, so both time-ordered types behave consistently. Crucial for `ids --type ulid --count 1000 > ids.txt` producing a genuinely sorted file — without monotonicity, the ~1000 IDs generated within the first few ms bucket would sort by random portion, not generation order.

  **Thread safety:** lock is uncontended in CLI single-process usage. Library callers using the generator from multiple threads get correctness for free.

  **Collision risk:** 80 bits of entropy per ms → birthday-collision probability at 10,000 IDs/ms is ~4 × 10⁻¹⁸. Practically impossible.

- **`NanoidGenerator`:** hand-rolled, ~30 lines, rejection-sampling to eliminate modulo bias.
  1. Resolve `opts.Alphabet.ToChars()` → `char[] chars`.
  2. Compute `byte mask = (byte)(NextPowerOfTwo(chars.Length) - 1)` — bit mask for rejection zone.
  3. Loop until `opts.Length` chars are produced:
     - Fill a buffer of extra bytes from `ISecureRandom.Fill`.
     - For each byte `b`: compute `b & mask`. If `< chars.Length`, emit `chars[b & mask]`. Otherwise, reject and continue.

  **Bias elimination:** the naive `byte % chars.Length` is unbiased when `chars.Length` divides 256 (i.e. alphabet size ∈ {16, 32, 64, 128, 256}), but slightly biased for 62 (`alphanum`). Rejection sampling using a power-of-two mask produces uniformly distributed output across any alphabet. Matches the reference NanoID implementations in JavaScript/Go/Rust.

  **Throughput:** rejection rate is `1 - (chars.Length / nextPow2)`:

  | Alphabet | Chars | nextPow2 | Reject rate | Avg bytes per output char |
  |---|---:|---:|---:|---:|
  | `hex` | 16 | 16 | 0 % | 1.00 |
  | `url-safe` | 64 | 64 | 0 % | 1.00 |
  | `alphanum` | 62 | 64 | 3.1 % | ~1.03 |
  | `lower` (a–z0–9) | 36 | 64 | 43.8 % | ~1.78 |
  | `upper` (A–Z0–9) | 36 | 64 | 43.8 % | ~1.78 |

  Plenty fast for any practical `--count` — the OS CSPRNG is cheap, and even `lower`/`upper` with ~44% rejection average under two byte-draws per output char.

**Factory:**

```csharp
public static IIdGenerator Create(IdType type) => type switch
{
    IdType.Uuid4  => new Uuid4Generator(),
    IdType.Uuid7  => new Uuid7Generator(),
    IdType.Ulid   => new UlidGenerator(SecureRandom.Default, SystemClock.Instance),
    IdType.Nanoid => new NanoidGenerator(SecureRandom.Default),
    _ => throw new ArgumentOutOfRangeException(nameof(type)),
};
```

Tests construct generators directly with fake clock / random sources to assert deterministic byte-exact output.

**`ArgParser`:**

- Parses argv via ShellKit's `CommandLineParser`, produces `IdsOptions` or `ParseResult` with error details.
- Runs the flag/type compatibility matrix after parsing.
- Pure function — returns rich result, does not touch I/O.

**`Formatting`:**

- `string FormatGuid(Guid, UuidFormat, bool uppercase)` — switch on format, then apply case.
- JSON element shaping for `--json` mode (writes one element; caller joins into an array).

### Console App (`ids`)

1. Parse argv → `ArgParser.Parse` → `IdsOptions` or error.
2. On parse/validation error → exit 125, usage hint to stderr.
3. If `--describe` → `DescribeJson.Emit()`, exit 0.
4. If `--help` / `--version` → emit, exit 0.
5. `var gen = IdGeneratorFactory.Create(opts.Type);`.
6. Loop `opts.Count` times:
   - Plain: write `gen.Generate(opts) + "\n"` to stdout.
   - `--json`: accumulate into a list, emit single JSON array at end (10k IDs ≈ 500 KB, safe to buffer).
7. Exit 0.

No async, no streams, no backpressure — IDs are small and the loop is bounded by `--count`.

---

## Data Flow

```
argv → ArgParser.Parse → IdsOptions ──→ ValidateCompatibility
                                             │
                                             ▼
                              IdGeneratorFactory.Create(opts.Type)
                                             │
                                             ▼
              ┌──────────── loop opts.Count times ────────────┐
              │  gen.Generate(opts) → string                  │
              │  opts.Json ? buffer : write "line\n" stdout   │
              └───────────────────────────────────────────────┘
                                             │
                                             ▼
                       opts.Json ? write JSON array : (done)
```

---

## Error Handling

| Condition | Exit | Stderr |
|---|---|---|
| Success | 0 | — |
| Unknown flag / malformed args | 125 | usage hint |
| Unknown `--type` value | 125 | `ids: unknown --type '<v>' (expected: uuid4, uuid7, ulid, nanoid)` |
| Unknown `--format` value | 125 | `ids: unknown --format '<v>' (expected: default, hex, braces, urn)` |
| Unknown `--alphabet` value | 125 | `ids: unknown --alphabet '<v>' (expected: url-safe, alphanum, hex, lower, upper)` |
| `--count ≤ 0` / `--length ≤ 0` | 125 | specific message per flag |
| Flag/type mismatch | 125 | see compatibility matrix |
| Internal / unexpected error | 126 | `ids: <message>` |

No 127 case — `ids` has no external helper to be missing, no process to spawn. Pure compute.

Exit-code convention matches the suite (125/126/127 for tool's own errors; 0 for success).

---

## Testing

xUnit, AOT-compatible (no reflection, no dynamic-proxy mocking — hand-written fakes for `ISystemClock` / `ISecureRandom`).

### `Winix.Codec.Tests`

- **`Base32CrockfordTests`:**
  - Round-trip for empty, 1-byte, 16-byte (ULID size), 32-byte, random 1 KB buffers.
  - Case-insensitive decode (`"ABC"` and `"abc"` decode identically).
  - Crockford alias handling on decode: `I`→`1`, `L`→`1`, `O`→`0`.
  - Reject invalid chars (`U`, `0x20`, emoji) with clear exception.
  - Known-vector pairs from the Crockford specification.
- **`SecureRandomTests`:**
  - Smoke test `Fill` produces non-zero bytes in a 32-byte span across 100 calls.
  - Asserts the entire span is written (guards against "forgot to fill" regressions).

### `Winix.Ids.Tests`

- **`ArgParserTests`** — table-driven. Every flag combination has a positive test (flags parse to expected `IdsOptions`) and every row of the compatibility matrix has a negative test (parses, validation rejects).
- **`FormattingTests`** — UUID formatting for all 8 combinations of `{Default, Hex, Braces, Urn} × {lowercase, uppercase}` against one known GUID input.
- **`Uuid4GeneratorTests`:**
  - 100 calls produce 100 distinct well-formed v4 strings (version nibble = 4, variant bits = 10).
- **`Uuid7GeneratorTests`:**
  - 100 calls produce 100 distinct well-formed v7 strings (version nibble = 7, variant bits = 10).
  - 1000 sequential calls → `string.CompareOrdinal(prev, next) <= 0` across every pair (time-ordering holds).
- **`UlidGeneratorTests`:**
  - **Deterministic output:** FakeClock at ms=42, FakeRandom returning known bytes → assert exact 26-char output string.
  - **Monotonicity within same ms:** FakeClock stuck at ms=42, 1000 calls → output sorts strictly ascending (each later call > prior).
  - **Rollover:** FakeClock advances to ms=43 → random portion is freshly drawn (not continued from ms=42's final state).
  - **Concurrent safety:** 8 threads × 1000 iterations → all 8000 IDs unique; within each observed ms bucket, generation order equals sort order.
- **`NanoidGeneratorTests`:**
  - **Deterministic output:** FakeRandom stream + known alphabet → assert exact output string for length 12, 21, 64.
  - **Length honoured:** `--length 1`, `--length 21`, `--length 100`, `--length 1000`.
  - **Rejection sampling:** with `alphanum` (62 chars), feed a random stream including bytes 248–255 (the "reject zone" under mask 63) → assert those bytes are rejected, not mapped into output. Prevents regression on the bias-elimination logic.
  - **Alphabet containment:** each named alphabet → output chars all in the alphabet set.
- **`IdGeneratorFactoryTests`** — each `IdType` value → correct concrete generator type.
- **`DescribeJsonTests`** — shape snapshot of the `--describe` output (matches the pattern used by every other Winix tool).
- **`JsonOutputTests`** — `--json` with each type, with `--count 1`, `--count 3`, produces well-formed JSON array with expected per-type fields.

### Integration (via `src/ids` process)

- `ids --help`, `--version`, `--describe` exit 0 with expected output shape.
- `ids --count 5` produces 5 lines + one trailing newline.
- `ids --type ulid --count 1000 | sort -c` succeeds (validates monotonicity end-to-end through the full stack).
- `ids --type nanoid --length 12` produces a 12-char string matching `[A-Za-z0-9_-]{12}`.

Target: ~80–100 tests (comparable density to `retry` at 47 and `when` at 145; `ids` is in between in complexity).

---

## Distribution

Mirrors every other Winix tool:

- **Scoop manifest:** `bucket/ids.json`.
- **Suite bundle:** add `ids` to `bucket/winix.json`'s `bin` array.
- **Release pipeline:** add `ids` to `.github/workflows/release.yml` — `dotnet publish` per `matrix.rid`, `dotnet pack`, per-tool zip (Linux/macOS + Windows), combined-zip `Copy-Item`, and the `tools:` map entry.
- **Post-publish:** add `update_manifest bucket/ids.json …` and `generate_manifests "ids" "Ids" "…"` to `.github/workflows/post-publish.yml`.
- **NuGet package ID:** `Winix.Ids` (add to CLAUDE.md list). `Winix.Codec` is a library-only package ID, consumed transitively.
- **Docs:** `src/ids/README.md`, `docs/ai/ids.md`, add to `llms.txt`, update CLAUDE.md project layout to include `src/Winix.Codec/` and `src/Winix.Ids/` + `src/ids/` + tests.
- **Man page:** `src/ids/man/man1/ids.1` (groff), referenced in `src/ids/ids.csproj`.

---

## v2 Scope (Deferred — Not In This Design)

From the tool ideas memory, all deferred for a later release:

- Additional UUID versions: v1 (MAC-based), v3 (MD5 namespace), v5 (SHA-1 namespace), v6 (field-reordered v1).
- Alternative ID types: CUID, CUID2, XID, KSUID, snowflake (needs machine-ID configuration — heavier).
- Arbitrary custom NanoID alphabets: `--alphabet <literal-string>` with validation (min 2 chars, warn on short alphabets).
- Namespace-based deterministic UUIDs (v3/v5 require a namespace + name input pair).

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|---|---|
| Custom NanoID alphabets (`--alphabet <literal>`) | Named presets cover practically all real usage; custom strings open validation edge cases (empty, single-char, duplicates) best handled together in v2. |
| UUID v1/v3/v5/v6 | Niche compared to v4/v7; v3/v5 need a namespace+name API which changes the flag shape. Bundle as "UUID extras" v2. |
| Snowflake | Requires machine-ID configuration (persistence, conflict avoidance) — too much infrastructure for a single-binary tool. |
| Slug-style human-readable IDs | Rejected scope (per memory) — belongs in a separate tool if ever. |
| Password / passphrase generation | Rejected — future `pass`/`pwgen` tool has separate requirements (word-list, copy-to-clipboard integration). |
