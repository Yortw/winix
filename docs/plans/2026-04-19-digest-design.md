# digest — Cryptographic Hashing and HMAC CLI

**Date:** 2026-04-19
**Status:** Proposed
**Project:** Winix (`D:\projects\winix`)

---

## Overview

`digest` is a cross-platform CLI for cryptographic hashing and HMAC. Single AOT-compiled binary with a consistent flag surface across algorithms, and deliberately safe HMAC key handling via four input modes that map to distinct threat models.

**Why it's needed:**

- **HMAC on Windows has no first-class tool.** Users shell to `openssl dgst -hmac ...` (if OpenSSL is installed) or write a 5-line PowerShell using the `HMAC*` classes. This is the primary gap `digest` fills.
- **Cross-platform consistency.** Linux ships `sha256sum`, `shasum`, `md5sum` in varying combinations. macOS has `shasum` but not `sha256sum`. Windows has `certutil -hashfile` (awkward output) or PowerShell `Get-FileHash` (slow startup, quirky formatting). No single tool covers all platforms with a stable flag surface.
- **Safe key handling is rare.** Most HMAC CLIs accept keys only via `--key <literal>`, which leaks to `ps`, shell history, `/proc/*/cmdline`, and system logs. `digest` makes `--key-env`, `--key-file`, and `--key-stdin` first-class; the unsafe `--key` literal emits a stderr warning.

**Primary use cases:**

- Hash a file: `digest path/to/file` → SHA-256 hex on stdout.
- Hash multiple files: `digest *.txt` → one sha256sum-compatible line per file.
- Hash a string literal: `digest "hello"` → SHA-256 of the UTF-8 bytes.
- Hash stdin: `curl ... | digest` or `digest < file`.
- HMAC a payload with a key from env/file/stdin: `digest --hmac sha256 --key-env API_SECRET "payload"`.
- Verify a known hash: `digest --verify "abc123..." file` → exit 0 if match, 1 if not.
- Compose with secret storage: `age --decrypt key.age | digest --hmac sha256 --key-stdin "payload"`.

**Positioning:**

- **vs `sha256sum`/`md5sum`**: single cross-platform binary, built-in HMAC, base64/base32 output, `--describe` metadata for AI agents.
- **vs `openssl dgst`**: much smaller binary, cleaner flag surface, safer default HMAC key handling, no OpenSSL dependency on Windows.
- **vs PowerShell `Get-FileHash`**: ~10× faster startup, AOT native binary, proper stdin handling.

**Platform:** Cross-platform (Windows, Linux, macOS). No platform-specific code except an optional Unix-only `--key-file` permissions check.

---

## Project Structure

```
src/Winix.Codec/                   — EXTEND existing shared library
  Base32Crockford.cs               — (existing, reused for --base32 output)
  SecureRandom.cs                  — (existing, not consumed by digest)
  ISecureRandom.cs                 — (existing)
  Hex.cs                           — NEW: Encode(ReadOnlySpan<byte>, bool upper) + Decode(string)
  Base64.cs                        — NEW: Encode(ReadOnlySpan<byte>, bool urlSafe) + Decode; thin wrapper over System.Convert for std, custom for URL-safe
  ConstantTimeCompare.cs           — NEW: BytesEqual(ReadOnlySpan<byte>, ReadOnlySpan<byte>) + StringEquals(string, string, bool caseInsensitive)

src/Winix.Digest/                  — class library
  HashAlgorithm.cs                 — enum: Sha256, Sha384, Sha512, Sha1, Md5, Sha3_256, Sha3_512, Blake2b
  OutputFormat.cs                  — enum: Hex, Base64, Base64Url, Base32
  InputSource.cs                   — sealed class hierarchy: StringInput, StdinInput, SingleFileInput, MultiFileInput
  DigestOptions.cs                 — parsed options record
  IHasher.cs                       — interface: byte[] Hash(ReadOnlySpan<byte>) + byte[] Hash(Stream)
  HashFactory.cs                   — HashAlgorithm -> IHasher using BCL primitives (+ Blake2Fast for BLAKE2b)
  HmacFactory.cs                   — HashAlgorithm + key -> IHasher (HMAC wrapper)
  KeyResolver.cs                   — resolves HMAC key from --key-env / --key-file / --key-stdin / --key; emits warnings
  KeyFilePermissionsCheck.cs       — Unix-only: warns if file is group/other readable
  HashRunner.cs                    — orchestrates InputSource -> IHasher -> OutputFormat -> result lines
  Verifier.cs                      — --verify mode with constant-time comparison
  Formatting.cs                    — sha256sum-compatible line, JSON element shaping
  ArgParser.cs                     — ShellKit-backed parser + compatibility matrix

src/digest/                        — thin console app
  Program.cs                       — argv -> ArgParser -> HashRunner -> stdout -> exit code
  digest.csproj                    — net10.0, PublishAot=true, PackAsTool, PackageId=Winix.Digest
  README.md
  man/man1/digest.1                — groff

tests/Winix.Codec.Tests/           — EXTEND
  HexTests.cs
  Base64Tests.cs
  ConstantTimeCompareTests.cs

tests/Winix.Digest.Tests/          — xUnit
  Fakes/FakeTextReader.cs          — controllable stdin
  Fakes/FakeKeySource.cs           — for KeyResolver tests where needed
  HashFactoryTests.cs              — RFC test vectors, all algorithms
  HmacFactoryTests.cs              — RFC 4231 test vectors
  KeyResolverTests.cs              — precedence, conflicts, warnings, newline stripping
  HashRunnerTests.cs               — string/stdin/single-file/multi-file modes
  VerifierTests.cs                 — match/mismatch, constant-time behaviour
  ArgParserTests.cs                — flag parsing + conflict matrix
  FormattingTests.cs               — plain text + sha256sum line + JSON

bucket/digest.json                 — scoop manifest
docs/ai/digest.md                  — AI agent guide
```

Two notes:

1. **`Winix.Codec` grows by three focused files.** All are codec/crypto primitives, matching the library's stated purpose. Future `pass`/`pwgen` tool will reuse `Hex` and the existing `SecureRandom`.

2. **`KeyFilePermissionsCheck.cs` is Unix-only.** Uses `File.GetUnixFileMode()` (BCL, AOT-safe). On Windows it's a no-op (Windows ACLs are hard to check succinctly, and DPAPI/the future `protect` tool is the better long-term answer).

---

## CLI Interface

### Examples

```bash
digest                               # SHA-256 of stdin (implicit default)
digest "hello"                       # SHA-256 of a literal string
digest --sha512 "hello"              # SHA-512 of a string
digest path/to/file                  # SHA-256 of a file
digest *.txt                         # one SHA-256 line per file (sha256sum-compatible)
digest --md5 legacy.bin              # MD5 with stderr warning
digest --base64 file.bin             # base64 output
digest --upper file.bin              # uppercase hex

# HMAC modes
digest --hmac sha256 --key-env API_SECRET "payload"
digest --hmac sha256 --key-file ~/.secret path/to/file
echo "payload" | digest --hmac sha256 --key-file ~/.secret
age --decrypt key.age | digest --hmac sha256 --key-stdin "payload"

# Verify
digest --verify "abc123..." file.bin
digest --hmac sha256 --key-env SECRET --verify "xyz..." file

# Metadata
digest --help / --version / --describe
```

### Flags

| Flag | Short | Default | Description |
|---|---|---|---|
| `--sha256` | | (default) | SHA-256 |
| `--sha384` | | | SHA-384 |
| `--sha512` | | | SHA-512 |
| `--sha1` | | | SHA-1 (stderr warning) |
| `--md5` | | | MD5 (stderr warning) |
| `--sha3-256` | | | SHA3-256 |
| `--sha3-512` | | | SHA3-512 |
| `--blake2b` | | | BLAKE2b-512 |
| `--algo ALGO` | `-a` | `sha256` | Alternative to individual flags |
| `--hmac ALGO` | | | HMAC mode (requires key source) |
| `--key-env VAR` | | | Read key from env var |
| `--key-file PATH` | | | Read key from file (Unix permission warning if group/other readable) |
| `--key-stdin` | | | Read key from stdin |
| `--key KEY` | | | Literal key (stderr warning) |
| `--key-raw` | | off | Preserve bytes on `--key-file`/`--key-stdin` (skip newline strip) |
| `--hex` | | (default) | Hex (lowercase) |
| `--base64` | | | Base64 (standard alphabet) |
| `--base64-url` | | | Base64 URL-safe variant |
| `--base32` | | | Crockford base32 (uppercase, no padding) |
| `--upper` | `-u` | off | Uppercase hex output |
| `--string` | | off | Treat positional as literal string (disable file auto-detect) |
| `--file` | | off | Treat positional as file path (error if not found) |
| `--verify EXPECTED` | | | Compare output constant-time; exit 0 match, 1 mismatch |
| `--json` | | off | JSON output |
| `--describe`/`--help`/`--version`/`--color`/`--no-color` | | | Standard Winix flags |

### Input mode resolution

`HashRunner` picks one mode from parsed options:

| Condition | Mode | Semantics |
|---|---|---|
| No positional args, stdin redirected | **Stdin** | Hash bytes read from stdin. |
| One positional arg, not a file path (and `--string` or auto-detect says string) | **String** | Hash UTF-8 bytes of literal. |
| One positional that is an existing file (or `--file`) | **SingleFile** | Streaming hash of file contents. |
| Multiple positional args | **MultiFile** | One hash per file; all-or-nothing validation up front. |
| One positional `-` | **Stdin** (explicit) | Same as stdin-redirected. |

**String/file disambiguation rules:**

1. If exactly one positional arg exists AND it matches `File.Exists()` → SingleFile.
2. Else if exactly one positional → String.
3. Else (multiple positionals) → MultiFile; every positional must exist as a file or we exit 125 **before** producing any output (all-or-nothing).

`--string` forces literal mode (bypasses file-exists check). `--file` forces file mode (errors if positional isn't an existing file). README must document this rule prominently — `digest file.txt` in a directory without `file.txt` silently hashes the string `"file.txt"` unless `--file` is specified.

### Streaming for files

Small files (≤ 64 KB): read into a buffer, call `SHA256.HashData(span)`.
Large files: `using var stream = File.OpenRead(path); return SHA256.HashData(stream);` — the `Stream`-accepting overload reads incrementally (8 KB at a time) without loading the whole file.

No memory-mapped I/O — BCL's Stream path is fast enough and avoids MMF complexity (Windows `FILE_MAP_READ` quirks, network-share edge cases).

### Output format composition

**Plain text, single input:**
```
<encoded-hash>\n
```

**Plain text, multi-file (sha256sum-compatible, binary-mode marker):**
```
<encoded-hash> *<filename>\n
<encoded-hash> *<filename>\n
```
Single space + `*` prefix on the filename. The `*` is `sha256sum`'s binary-mode marker: the hash was computed over the file's raw bytes with no CR/LF translation. We always use this form — `digest` never does text-mode CR/LF translation, so the marker honestly describes our behaviour. `sha256sum -c SHA256SUMS` accepts both `*` and two-space markers, so existing verification flows remain compatible. Users generating checkfiles on Windows and verifying on Linux (or vice versa) get matching hashes because both sides agree on binary-mode semantics.

**JSON single input:**
```json
{"algorithm":"sha256","format":"hex","hash":"2cf24dba...","source":"string"}
```

**JSON multi-file:**
```json
[{"algorithm":"sha256","format":"hex","hash":"...","source":"file","path":"a.txt"},
 {"algorithm":"sha256","format":"hex","hash":"...","source":"file","path":"b.txt"}]
```

Plain-text output is the hash only — no algorithm prefix — matching `sha256sum`'s line format. In JSON mode the `"algorithm"` field uses the `hmac-sha256` / `hmac-sha512` / etc. identifier when HMAC mode is active. Error messages and `--describe` output use the same `hmac-<algo>` identifier.

---

## Algorithms and HMAC

### Hash dispatch

All hash primitives from .NET BCL `System.Security.Cryptography` static methods:

| Algorithm | Source |
|---|---|
| SHA-256 / SHA-384 / SHA-512 | `SHAxxx.HashData` |
| SHA-1 | `SHA1.HashData` (warning) |
| MD5 | `MD5.HashData` (warning) |
| SHA3-256 / SHA3-512 | `SHA3_xxx.HashData` (.NET 8+) |
| BLAKE2b-512 | `Blake2Fast.Blake2b` (NuGet dep) |

**SHA-3 runtime check:** `SHA3_256.IsSupported == false` on older OSes lacking a compatible crypto backend. When the user requests SHA-3 on an unsupported platform, exit 126 with `digest: SHA-3 is not available on this platform (OS crypto backend missing)` rather than letting `PlatformNotSupportedException` propagate.

**BLAKE2b dependency: `Blake2Fast.Blake2b`** — tiny (~2 KB), pure-managed, AOT-friendly, MIT licensed. First external crypto dep in Winix; reviewed carefully in the ADR.

### HMAC dispatch

All HMACs from BCL static methods:

| Algorithm | Source |
|---|---|
| HMAC-SHA-256/384/512 | `HMACSHAxxx.HashData(key, data)` |
| HMAC-SHA-1 | `HMACSHA1.HashData` (warning — see ADR for why still kept) |
| HMAC-MD5 | `HMACMD5.HashData` (warning) |
| HMAC-SHA3-256/512 | `HMACSHA3_xxx.HashData` |
| HMAC-BLAKE2b | Native keyed-BLAKE2b mode from `Blake2Fast` (RFC 7693 §2.9, not a wrapper) |

HMAC-SHA-1 and HMAC-MD5 are retained because HMAC construction doesn't share the direct-hash collision vulnerabilities. AWS Signature v4 still uses HMAC-SHA-1. Removing them would break real users for no safety gain.

### Key resolution (the heart of the security story)

`KeyResolver.Resolve(DigestOptions) -> byte[]` runs these checks in order:

1. **Exactly-one-source rule.** Exactly one of `--key-env`, `--key-file`, `--key-stdin`, `--key` must be present when `--hmac` is set. Zero or multiple → exit 125.

2. **Payload/key stdin conflict.** `--key-stdin` requires the payload to NOT come from stdin. Both wanting stdin → exit 125.

3. **`--key` literal warning.** Every invocation with `--key <literal>` emits:
   ```
   digest: warning: --key exposes the key via 'ps', shell history, and process listings.
           Prefer --key-env, --key-file, or --key-stdin for non-ephemeral scripts.
   ```
   No flag to suppress — the warning is the point.

4. **`--key-file` Unix permission check.** If `(mode & (S_IRGRP | S_IROTH)) != 0`, emit:
   ```
   digest: warning: /path/to/secret has mode 0644 and is readable by group/other.
           Consider 'chmod 0600 /path/to/secret'.
   ```
   No hard fail. Uses `File.GetUnixFileMode(path)`. Windows skipped.

5. **Byte decoding.**
   - Env var and `--key` literal: decoded from UTF-8 string (ConsoleEnv.UseUtf8Streams handles this).
   - File and stdin: raw bytes, but by default **strip one trailing `\n` or `\r\n`**. This matches clip's asymmetric newline behaviour — `echo "secret" > keyfile` produces a 6-byte key, not 7. `--key-raw` preserves exact bytes.

### Exactly-one-stdin rule

`digest` has two potential stdin consumers: payload (when no positional file arg) and `--key-stdin`. Exactly one can use stdin per invocation. ArgParser validates up front.

---

## Verify mode

`--verify <expected>` compares the computed hash against the expected string using `ConstantTimeCompare.StringEquals()`:
- **Case-insensitive** for hex output.
- **Case-sensitive** for base64/base32 output.

**Exit codes:**
- Match → exit 0, no stdout.
- Mismatch → exit 1, stderr `digest: verification failed`.

**Constraint:** single-input modes only (string, stdin, single file). `--verify` + multi-file → exit 125. Multi-file verification (sha256sum `--check` semantics) is deferred to v2.

---

## Error Handling

| Code | Condition | Stream |
|---|---|---|
| 0 | Success (hash printed, or verify matched) | — |
| 1 | Verify mismatch | stderr: `digest: verification failed` |
| 125 | Usage error | stderr: specific message + usage hint |
| 126 | Runtime error (SHA-3 missing, file read failure, unexpected exception) | stderr: `digest: error: <message>` |

Exit code 1 for verify mismatch (not 125) matches `grep`/`diff`/`cmp` convention — the tool succeeded, the answer was negative.

**Conflict matrix** (all exit 125 unless noted):

| Condition | Message |
|---|---|
| Multiple algorithm flags (e.g., `--sha256 --sha512`) | `digest: multiple algorithms specified — choose one` |
| Unknown `--algo` value | `digest: unknown algorithm '<v>' (expected: sha256, sha384, sha512, sha1, md5, sha3-256, sha3-512, blake2b)` |
| `--hmac` combined with `--sha256`/`--sha512`/`--algo`/etc. | `digest: --hmac carries its own algorithm; do not combine with --sha256 / --algo / etc.` |
| `--hmac` without key source | `digest: --hmac requires one of --key-env, --key-file, --key-stdin, --key` |
| Multiple key sources with `--hmac` | `digest: exactly one of --key-env, --key-file, --key-stdin, --key must be specified` |
| `--key-env` unset | `digest: environment variable 'VAR' is not set` |
| `--key-file` missing | `digest: key file '<path>' not found` |
| `--key-file` permission denied (exit 126) | `digest: cannot read '<path>': permission denied` |
| `--key-stdin` + stdin payload | `digest: --key-stdin cannot be combined with stdin payload` |
| `--verify` + multi-file | `digest: --verify is not supported with multiple files` |
| Multiple output formats | `digest: multiple output formats specified — choose one` |
| `--file` but arg isn't a file | `digest: '<arg>' is not a file` |
| SHA-3 unsupported on platform (exit 126) | `digest: SHA-3 is not available on this platform (OS crypto backend missing)` |

**Warnings (exit 0, emit to stderr, don't block execution):**
- `--key` literal used.
- MD5 or SHA-1 used (bare or HMAC).
- `--key-file` group/other readable on Unix.

### Broken-pipe

Matches the `ids` pattern: catch `IOException` in the output loop, exit 0 silently. Rare for digest (output is short) but `digest *.log | head` is plausible.

---

## Testing

**`Winix.Codec.Tests` — ~18 tests across 3 new files.**

- `HexTests`: encode known vectors, lowercase default, `--upper` produces uppercase, decode round-trip, decode accepts mixed case, decode rejects odd length + non-hex chars.
- `Base64Tests`: standard round-trip, URL-safe variant uses `-`/`_`, empty round-trip, decode rejects invalid.
- `ConstantTimeCompareTests`: equal bytes true, unequal same-length false, unequal different-length false, string variant null-safe, structural test that comparison doesn't short-circuit on first mismatch.

**`Winix.Digest.Tests` — ~130 tests across 8 files.**

- `HashFactoryTests`: 3 known vectors × 8 algorithms (24 tests), platform-conditional skip for SHA-3 if `IsSupported == false`.
- `HmacFactoryTests`: RFC 4231 vectors for HMAC-SHA-256/384/512; smaller vector set for HMAC-SHA-1 (RFC 2202) and HMAC-MD5 (RFC 2104); key-longer-than-block-size edge case; zero-length key.
- `KeyResolverTests`: each source resolves correctly; zero/multiple sources error; stdin conflict detection; trailing-newline strip by default; `--key-raw` preserves bytes; `--key` literal emits warning (capture via `TextWriter`); Unix permission warning (skip on Windows); missing env var/key file errors.
- `HashRunnerTests`: string/stdin/single-file/multi-file modes each produce correct hash; all-or-nothing validation on multi-file; `--string` and `--file` override auto-detect.
- `VerifierTests`: match/mismatch, case handling per format, structural constant-time, `--verify` + multi-file errors.
- `ArgParserTests`: positive parses for every flag; every row of the conflict matrix.
- `FormattingTests`: plain text single-input, sha256sum line format, JSON single, JSON array, HMAC algorithm prefix.
- `FakeTextReader` / `FakeKeySource` test doubles.

Test infrastructure: `Path.GetTempFileName` + `try/finally` cleanup for file-based tests; `Environment.SetEnvironmentVariable` (process-scoped, parallel-safe with unique var names).

All tests xUnit, AOT-compatible (no reflection, no dynamic-proxy mocking — hand-written fakes).

---

## Distribution

Standard Winix pattern:

- **Scoop manifest:** `bucket/digest.json`.
- **Suite bundle:** add `digest` to `bucket/winix.json`'s `bin` array.
- **Release pipeline:** add `digest` to `.github/workflows/release.yml` (per-RID `dotnet publish`, `dotnet pack`, per-tool zip, combined-zip, `tools:` map).
- **Post-publish:** add `update_manifest bucket/digest.json …` and `generate_manifests "digest" "Digest" "…"` to `.github/workflows/post-publish.yml`.
- **NuGet:** `Winix.Digest` (library-only `Winix.Digest.Library` consumed transitively).
- **Docs:** `src/digest/README.md`, `docs/ai/digest.md`, `llms.txt`, CLAUDE.md project layout.
- **Man page:** `src/digest/man/man1/digest.1`.

---

## v2 Scope (Deferred — Not In This Design)

- **BLAKE3** — deferred pending AOT-compatible .NET implementation review. Current candidates (`Blake3.NET`) claim AOT but aren't battle-tested.
- **`--verify-from <checkfile>`** — sha256sum-compatible multi-file verification (reads `<hash>  <filename>` lines, checks each). v2 if user demand appears.
- **`--output <file>`** — write output to a file instead of stdout. Currently users use shell redirect (`> file`); explicit flag avoids quoting issues in some shells.
- **`--recursive`** — recurse into directories like `sha256sum -r`. Conflicts with our explicit list semantics; defer.

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|---|---|
| **Encrypted-at-rest key files** | `--key-file` reads unprotected bytes. Users who want encrypted-at-rest keys should pipe through their existing secret store via `--key-stdin`: `age --decrypt key.age \| digest --hmac sha256 --key-stdin`, `security find-generic-password -w \| digest --key-stdin` (macOS), `secret-tool lookup … \| digest --key-stdin` (Linux), `pass show … \| digest --key-stdin`. A future Winix `protect`/`unprotect` tool (now in Tier 1 of the tool ideas list) would unify this cross-platform via DPAPI on Windows + `security` shell-out on macOS + `secret-tool` shell-out on Linux. `digest` does not build encryption in itself. |
| **BLAKE3 support** | No AOT-vetted .NET implementation as of 2026-04-19. Revisit for v2 when `Blake3.NET` has shipped AOT smoke tests or equivalent. |
| **sha256sum `--check` compatibility** | `--verify <expected>` covers the 80% case (verifying one download). Multi-file checkfile verification is a larger surface (format parsing, per-file status reporting, failure modes) that deserves its own v2 design. |
| **CRC32 / xxHash / non-cryptographic hashes** | Digest is a cryptographic tool. Non-crypto hashing is a different market (fast integrity checks, hash tables) and would blur the tool's positioning. Separate tool if ever. |
| **Directory hashing / content-addressed storage** | Beyond digest's scope — belongs in a separate content-addressing tool, which would itself be a larger design than this. |
| **Output redaction of HMAC keys in JSON `--describe`** | `--describe` output doesn't include runtime state (keys, hashes), only static metadata. Not an issue for the current design. |
