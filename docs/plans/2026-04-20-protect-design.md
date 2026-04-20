# protect / unprotect — Design

**Date:** 2026-04-20
**Tools:** `protect`, `unprotect`
**Release:** v0.4.0 (in progress)
**Status:** Approved

## Goal

Cross-platform encrypt-at-rest CLI that wraps each operating system's native secret-storage primitive. The `protect` command encrypts a stream or file; `unprotect` reverses it. Zero key management is required from the user — the OS provides and derives the key.

On Windows, `protect` is a direct CLI for DPAPI (the genuine gap this tool fills). On macOS and Linux, where no native "encrypt arbitrary stream with OS key" primitive exists, `protect` implements the same UX via AEAD (AES-256-GCM) with a key stored in Keychain / libsecret respectively. The user never sees or manages that key.

Files are **explicitly not portable** between machines or users — they're scoped to "this user on this machine" (user scope, default) or "this machine" (machine scope, Windows + macOS only).

## Positioning

Fills a tool-shaped hole that exists on every platform:

- **Windows**: DPAPI is a widely-known OS primitive with no convenient CLI wrapper. `cmdkey` manages named credentials; it doesn't encrypt files. PowerShell `ConvertTo-SecureString` is PowerShell-only. Third-party .NET wrappers require running a custom program. `protect` is the missing native-DPAPI CLI.
- **macOS**: `security` manages Keychain items (named credentials), not files. `hdiutil` creates encrypted disk images (containers, not single files). FileVault is whole-disk. No cross-platform-consistent file-level equivalent.
- **Linux**: `secret-tool` stores named secrets via libsecret; no file encryption. Whole-disk (LUKS) and filesystem-level (fscrypt, ecryptfs) tools don't solve the "encrypt this one file with my login credentials" workflow.

The closest existing tool is [sops](https://github.com/getsops/sops), which file-encrypts structured data (YAML/JSON/ENV) using external KMS or age/PGP keys. `protect` differs in two important ways: it targets OS-native keys (zero user key management) and it handles opaque binary content, not structured formats.

**Composition:**

```bash
# Protect an HMAC key file; use it with digest
protect api.key --rm                              # creates api.key.prot, removes original
unprotect < api.key.prot | digest --hmac sha256 --key-stdin "payload"

# Encrypt a config file in place
protect config.json --in-place

# Service / debug scenario on Windows
protect --scope machine service-config.xml
```

**Explicit non-goal:** portable encryption. Users who need that have `age`, `gpg`, or `sops`. `protect`'s value proposition is OS-native convenience, not portability.

## CLI Interface

```
protect   [OPTIONS] [FILE]
unprotect [OPTIONS] [FILE]
```

**Positional + piping:**

- `protect FILE` → encrypts to `FILE.prot`, keeps `FILE` by default
- `protect FILE -o OUT.prot` → explicit output path
- `protect FILE --in-place` → encrypts over `FILE` (safety pattern: temp + verify + atomic rename)
- `protect < stream > out.prot` → pure streaming
- `unprotect FILE.prot` → decrypts to `FILE` (strips `.prot` suffix)
- `unprotect FILE.prot -o OUT` → explicit output path

**Flags:**

| Flag | Default | Description |
|---|---|---|
| `-o PATH` / `--output PATH` | stdout, or `FILE.prot` for file-operand mode | Explicit output path. Refused if equal to input path. |
| `--in-place` | off | Encrypt/decrypt over the input file. Temp write + round-trip verify + atomic rename. |
| `--rm` / `--remove-source` | off | Delete source after successful verification. |
| `--keep` / `-k` | (implicit default) | Explicit keep — redundant but useful in scripts for clarity. |
| `--scope {user,machine}` | `user` | Key-derivation scope. Windows: DPAPI `CurrentUser` / `LocalMachine`. macOS: login Keychain / System Keychain. Linux: `user` only (`machine` fails fast — see below). |
| `--no-verify` | off | Skip round-trip verification. Faster but loses the belt-and-braces integrity check. Only meaningful for encryption. |
| `--color` / `--no-color` | — | Respect `NO_COLOR`. |
| Standard | | `--help`, `--version`, `--describe`. |

**Exit codes** (suite convention):

| Code | Meaning |
|---|---|
| 0 | Success. |
| 125 | Usage error — bad flags, path collision (`-o` == input), `--scope machine` on Linux, malformed CLI. |
| 126 | Runtime error — decryption failure, wrong-platform/scope file, key store unavailable, permission denied, round-trip verify failed. |

## File Format

```
Offset  Size  Field
  0     4     Magic: "WPRT" (0x57 0x50 0x52 0x54)
  4     1     Format version: 0x01
  5     1     Platform marker (table below)
  6     …     Chunk stream (format varies by platform)
```

**Platform marker values:**

| Value | Platform | v1 | Notes |
|---|---|---|---|
| 0x01 | Windows DPAPI `CurrentUser` | ✓ | — |
| 0x02 | Windows DPAPI `LocalMachine` | ✓ | — |
| 0x10 | macOS Keychain login (user) | ✓ | AES-256-GCM, key from Keychain |
| 0x11 | macOS Keychain System (machine) | ✓ | AES-256-GCM, requires root to read/write |
| 0x20 | Linux libsecret (user session) | ✓ | AES-256-GCM, key from libsecret |
| 0x21 | Linux systemd-creds (machine) | — | Reserved for v2. |

**Chunk stream format (unified — always chunked, 1 chunk for small files):**

Both DPAPI and AEAD paths use a 64 KB plaintext chunk size (last chunk may be smaller). This means there is no single-shot vs streaming special case; a small file is just a one-chunk stream.

### AEAD path (platform markers 0x10, 0x11, 0x20)

```
Per chunk:
  is_final(1 byte)    — 0x00 for intermediate chunks, 0x01 for final
  iv(12 bytes)        — cryptographically random, unique per chunk
  length(4 bytes BE)  — ciphertext byte length
  ciphertext(length)  — AES-256-GCM ciphertext of ≤ 64 KB plaintext
  tag(16 bytes)       — GCM authentication tag

Per-chunk AAD (fed to AesGcm.Encrypt/Decrypt):
  header(6 bytes)     — the file's magic|version|platform
  chunk_index(8 bytes BE)
  is_final(1 byte)    — same value as the leading byte of the chunk
```

The AAD binds each chunk to (a) the file header, (b) its position in the stream, and (c) whether it's terminal. Result:
- Tampering with the header: fails all chunks.
- Reordering chunks: fails the swapped chunks.
- Truncation (dropping the final chunk): the new last chunk has `is_final=false`, which doesn't match what the reader expects at EOF — decoder errors `"stream truncated"`.
- Swapping a final chunk with a non-final one: AAD mismatch, fails.

### DPAPI path (platform markers 0x01, 0x02)

```
Per chunk:
  length(4 bytes BE)  — DPAPI blob byte length
  dpapi_blob(length)  — ProtectedData.Protect output

The plaintext fed to DPAPI for each chunk is prefixed:
  is_final(1 byte) | plaintext_chunk(≤ 64 KB)
```

DPAPI has no AAD concept, so `is_final` is instead included *inside* the DPAPI-protected plaintext. The DPAPI tag covers it. Truncation detection: same as AEAD — last-visible-chunk's `is_final=false` → `"stream truncated"`.

### Single-chunk files

A file with ≤ 64 KB plaintext is exactly one chunk. Framing overhead is ~33 bytes for AEAD or ~5 bytes + DPAPI overhead for DPAPI path. Under 0.1% for a typical file.

## Architecture

```
src/Winix.SecretStore/                    — NEW shared library (consumed by protect AND envvault)
  ISecretStore.cs                         — Set/Get/Delete(namespace, key, byteArray); List(namespace) for envvault
  NullSecretStore.cs                      — in-memory; test fixtures
  WindowsCredentialManagerStore.cs        — P/Invoke to advapi32 CredRead/Write/Delete (used by envvault only — NOT by protect on Windows, which uses DPAPI directly)
  MacOsKeychainStore.cs                   — shell-out to `security find-generic-password`, `add-generic-password`, `delete-generic-password`
  LinuxLibsecretStore.cs                  — shell-out to `secret-tool store`, `lookup`, `clear`
  SecretStoreFactory.cs                   — selects backend by OS

src/Winix.Protect/                        — class library
  SubCommand.cs                           — enum: Protect, Unprotect
  Scope.cs                                — enum: User, Machine
  PlatformMarker.cs                       — enum mapping to byte values 0x01/0x02/0x10/0x11/0x20
  ProtectOptions.cs                       — sealed record: SubCommand, input/output paths, Scope, flags
  ArgParser.cs                            — argv → ProtectOptions; dispatches Protect vs Unprotect from argv[0] of the binary
  Header.cs                               — read/write the 6-byte header
  ChunkWriter.cs                          — stream → chunked output (format-agnostic; delegates per-chunk to backend)
  ChunkReader.cs                          — chunked input → stream; detects truncation via is_final invariant
  IProtectBackend.cs                      — EncryptChunk(plaintext, aadContext, isFinal) → chunk bytes; DecryptChunk(chunk, aadContext) → (plaintext, isFinal)
  DpapiBackend.cs                         — Windows only; wraps `ProtectedData.Protect`/`Unprotect`; no SecretStore
  AeadBackend.cs                          — abstract: AES-256-GCM per chunk with AAD; concrete per-platform subclasses inject SecretStore
  AeadKeychainBackend.cs                  — macOS; gets key from MacOsKeychainStore (auto-generates on first use)
  AeadLibsecretBackend.cs                 — Linux; gets key from LinuxLibsecretStore (auto-generates on first use)
  BackendFactory.cs                       — selects backend from Scope + platform; throws helpful errors for unsupported combos
  InPlaceExecutor.cs                      — temp-write + round-trip-verify + atomic rename orchestration
  RoundTripVerifier.cs                    — incremental SHA-256 during encrypt + during streaming decrypt; compares at end
  Formatting.cs                           — error-message composition

src/protect/
  Program.cs                              — thin orchestrator; dispatches Protect vs Unprotect based on argv[0]
  protect.csproj                          — AOT, PackAsTool, ToolCommandName=protect, PackageId=Winix.Protect
  unprotect.csproj                        — AOT, PackAsTool, ToolCommandName=unprotect, PackageId=Winix.Unprotect
                                            (both csprojs reference the same Winix.Protect library; Program.cs inspects its own name)
  README.md
  man/man1/protect.1
  man/man1/unprotect.1

tests/Winix.SecretStore.Tests/
  NullSecretStoreTests.cs                 — contract tests
  (platform-specific backend tests run as integration smoke tests on the corresponding CI runner, not required for green)
tests/Winix.Protect.Tests/
  HeaderTests.cs
  ChunkWriterReaderTests.cs               — round-trip with NullSecretStore + AeadBackend; small / medium / large
  InPlaceExecutorTests.cs                 — happy path, verify-failure preserves source, simulated crash cleanup
  ArgParserTests.cs
  RoundTripVerifierTests.cs
  FormattingTests.cs
```

**Key design points:**

- **Windows `protect` does NOT use SecretStore.** DPAPI is keyless from the user's perspective — the OS derives the key from user/machine credentials internally. `DpapiBackend` calls `ProtectedData.Protect` directly. SecretStore is for `envvault` and for Mac/Linux AEAD key storage; Windows `protect` skips it entirely. This is the whole point of Path Y: Windows is genuinely a DPAPI CLI.
- **Mac and Linux `protect` use SecretStore to store a synthesized 32-byte AES-256 key**, auto-generated on first encryption, under service/namespace `winix-protect` and account/key name `default-user-v1` (or `default-machine-v1` on Mac machine scope). Users never see or manage this key; it's conceptually the same as DPAPI on Windows (OS-protected, not visible to the user) — just implemented differently because Mac and Linux lack a direct DPAPI-like primitive.
- **SecretStore is extracted from day one** because `envvault` (next tool) will consume the exact same `ISecretStore` interface. Matches the `Winix.QrCode` / `Winix.FileWalk` / `Winix.Codec` precedent of extract-on-day-one when the second consumer is planned and proximate.
- **Unified chunked format** means one codepath for all file sizes. 64 KB chunk size is hard-coded in v1. `--chunk-size N` deferred to v2 if anyone needs it.
- **`IProtectBackend` is chunk-level**, not file-level. This lets `ChunkWriter` and `ChunkReader` handle the stream orchestration (EOF detection, truncation protection, AAD construction) uniformly while the backend handles per-chunk crypto. Keeps backends small.
- **`InPlaceExecutor` is orthogonal to backends.** It handles temp-file naming, atomic rename (via `File.Move(source, dest, overwrite: true)` on .NET which delegates to `MoveFileEx` / `rename()`), and the round-trip verification gate. Any backend can be used in-place or not.
- **`BackendFactory.Create(scope, onLinux)` encapsulates platform dispatch** and the scope/platform validity matrix. `Scope.Machine` + Linux throws `PlatformNotSupportedException` with a clear message caught by Program.cs and turned into exit 125.

## Algorithm Choices

### AEAD (Mac, Linux)
- **AES-256-GCM** via `System.Security.Cryptography.AesGcm` (BCL, AOT-clean since .NET 8).
- **Key size:** 256 bits.
- **IV size:** 12 bytes (GCM standard), cryptographically random per chunk via `RandomNumberGenerator.Fill`.
- **Tag size:** 16 bytes.
- **Key lifecycle:** generated once on first encryption via `RandomNumberGenerator.Fill(32)`, stored in Keychain / libsecret under `winix-protect` / `default-user-v1`. Retrieved for all subsequent encrypt and decrypt operations. Persists across invocations.

### DPAPI (Windows)
- `System.Security.Cryptography.ProtectedData.Protect(plaintext, optionalEntropy: null, scope)`.
- Scope: `DataProtectionScope.CurrentUser` for `--scope user`, `DataProtectionScope.LocalMachine` for `--scope machine`.
- No `optionalEntropy` in v1. Reserved for v2 named-key support.

### Round-Trip Verification
- During encrypt: incremental SHA-256 of the source stream bytes (via `IncrementalHash.CreateHash(HashAlgorithmName.SHA256)`).
- After encrypt: re-open the encrypted output, stream-decrypt it (same chunked-format reader), incremental SHA-256 on the decrypted bytes.
- Compare SHA-256 digests at EOF. Mismatch → delete output, preserve source, exit 126 with `"Encryption integrity check failed. Source file preserved. This is a bug; please report."`.
- Unlike sops (which does no round-trip check, trusting the AEAD primitive), we add this as defensive insurance against bugs in *our* chunking/framing/AAD-construction code. Cost is one extra decrypt pass (`--no-verify` opt-out available).

### Atomic Rename
- `File.Move(source, dest, overwrite: true)` under .NET 10. This maps to `MoveFileEx(src, dest, MOVEFILE_REPLACE_EXISTING)` on Windows and `rename(2)` on POSIX — both atomic for same-volume moves.
- Cross-volume renames not supported in v1; `InPlaceExecutor` creates the temp file in the *same directory* as the target to guarantee same-volume.

## Edge Cases

- **Input path == output path.** Refused at ArgParser — exit 125, `"Input and output paths are the same. Use '-o different-path' or '--in-place'."`
- **Shell redirection to same file (`protect < f > f`).** Not detectable — shell truncates `f` before `protect` starts. Documented in man page; recommend file-operand or `-o` form. Not a bug in `protect` per se, but a trap in shell semantics.
- **Decrypting a file encrypted with a different scope than requested.** Reader inspects platform marker byte; if it indicates a different scope than `--scope`, exit 126 with `"This file was encrypted with scope 'machine' but '--scope user' was given. Retry with '--scope machine'."`
- **Decrypting a file from a different platform.** Reader inspects platform marker; exit 126 with `"This file was encrypted on {platform} and cannot be decrypted on this machine."`
- **Decrypting a Windows DPAPI file under a different Windows user.** DPAPI throws `CryptographicException` on `Unprotect`. Mapped to exit 126 with `"Decryption failed — this file was encrypted by a different user or on a different machine."` (DPAPI doesn't distinguish between the two causes at the API level.)
- **Linux + `--scope machine`.** Fail fast in ArgParser before doing anything — exit 125 with `"Machine scope is not supported on Linux. Install systemd-creds or use user scope."`
- **macOS + `--scope machine` without root.** `security` CLI returns "User interaction is not allowed" or similar. Mapped to exit 126: `"Requires root privileges on macOS. Re-run with 'sudo protect --scope machine ...'."`
- **Keychain / libsecret not available at runtime.** Platform-specific install hints: `"libsecret-tools not installed. Install with 'sudo apt install libsecret-tools' (Debian/Ubuntu), 'sudo dnf install libsecret' (Fedora), or equivalent."`
- **Round-trip verification fails.** Exit 126, source preserved, encrypted output deleted. Log the mismatch (hashes in hex) to stderr for bug reports.
- **In-place mode on a symlink.** v1 refuses — `"'--in-place' is not supported on symlinks in v1 (path resolution + atomic rename interaction is a trap). Use explicit input/output paths."`
- **Truncation attack (final chunk dropped from ciphertext).** Detected at decrypt: the newly-final chunk has `is_final=false`, exit 126 with `"Ciphertext is truncated (final chunk missing)."`
- **Chunk reordering attack.** Detected at decrypt via AAD chunk_index mismatch: `"Ciphertext is corrupt: chunk order violation."`

## Error Handling

Standard suite mapping:
- 0: success
- 125: usage errors (caught before any I/O)
- 126: runtime errors (crypto failures, OS store errors, permission denials)
- `Formatting.UsageError(msg)`, `Formatting.RuntimeError(msg)` prefix with `"protect: "` (or `"unprotect: "` based on the invocation).

## Testing

Target ~100 tests.

- **`HeaderTests`** — magic bytes, version byte, platform-marker round-trips, reject invalid magic/version.
- **`ChunkWriterReaderTests`** — small (empty, 1-byte, 100-byte), medium (100 KB, crossing 1 chunk), large (10 MB, many chunks); AAD tampering detection, chunk reorder detection, truncation detection. Uses `NullSecretStore` + `AeadBackend` so tests are platform-independent.
- **`InPlaceExecutorTests`** — happy path, simulated encrypt-failure preserves source, simulated verify-failure preserves source + deletes temp, simulated crash before rename cleans temp on next run.
- **`ArgParserTests`** — flag parsing, path-collision refusal, scope/platform validity matrix, protect-vs-unprotect dispatch, `--in-place` + `-o` mutual exclusion, `--rm` + `--in-place` interaction (`--rm` implied by `--in-place`).
- **`RoundTripVerifierTests`** — matching hashes pass, mismatched hashes fail, incremental hashing correctness on chunked input.
- **`FormattingTests`** — error-message composition, protect-vs-unprotect prefix selection.

Platform-native backends (`DpapiBackend`, `AeadKeychainBackend`, `AeadLibsecretBackend`) are covered by **integration smoke tests** run only on the matching platform's CI runner — not required for overall CI green, but caught before release. Covers: encrypt-decrypt round-trip against the real OS store, scope switching, permission denied paths.

## Distribution

Follows suite conventions (reference: `qr` / `digest` / `notify`):

- `bucket/protect.json`, `bucket/unprotect.json` — scoop manifests
- `.github/workflows/release.yml` — per-RID publish, pack, zip, combined, tool-map entries for both `protect` and `unprotect`
- `.github/workflows/post-publish.yml` — `update_manifest` + `generate_manifests` lines for both, with per-tool winget tags (`encryption,dpapi,keychain,libsecret,at-rest,crypto` per-tool, plus the shared baseline)
- NuGet package IDs: `Winix.Protect`, `Winix.Unprotect`. Both install a single tool command matching the package.
- Shared lib `Winix.SecretStore` stays internal (matches `Winix.Codec` / `Winix.FileWalk` / `Winix.QrCode` precedent)
- `docs/ai/protect.md` + `docs/ai/unprotect.md` AI agent guides
- Entries in `llms.txt`
- `src/protect/README.md` + man pages for both commands
- CLAUDE.md updates: project layout, NuGet package IDs list (`Winix.Protect`, `Winix.Unprotect`), scoop manifests list (`protect.json`, `unprotect.json`)
- Package tags per the post-qr convention (shared baseline + domain tags)

## Out of Scope (v1)

- **Portable encryption.** Not a goal; use `age` / `gpg` / `sops` for that workflow.
- **Named keys** (`--key NAME`). Single default key per scope in v1. Rotation / per-context keys come in v2.
- **Additional AAD** (`--aad STRING` user-supplied binding). Header bytes serve as implicit AAD; explicit user AAD is v2.
- **Configurable chunk size** (`--chunk-size N`). Hard-coded 64 KB in v1.
- **Compression integration.** Use `squeeze` in a pipe.
- **Password-based fallback.** OS store is the whole point; no opt-out.
- **Batch mode** (multi-file per invocation). Shell `for` loop covers it.
- **In-place on symlinks.** Deferred; too many corner cases with atomic rename + symlink resolution.
- **Linux machine scope.** Reserved platform marker 0x21 for systemd-creds; v2.

## Open Implementation Questions

1. **Exact P/Invoke signatures for `CredRead`/`CredWrite`/`CredDelete`** on Windows Credential Manager (for `WindowsCredentialManagerStore`, consumed by `envvault` — not protect, but same library). Verify against current SDK; AOT clean but pin it.
2. **`security` CLI exit-code and stderr semantics** on macOS. Map "item not found" vs "access denied" vs "user interaction not allowed" to specific user-facing errors.
3. **`secret-tool` availability detection and install-hint phrasing** per distro. Detect via `which secret-tool`; message covers Debian/Ubuntu, Fedora/RHEL, Arch, openSUSE.
4. **Atomic rename fallback on cross-volume scenarios.** Document that in-place requires target directory = temp directory; refuse explicit cross-volume combinations during plan.
5. **Program.cs argv[0] inspection mechanics.** Use `Environment.ProcessPath` (absolute path to the running executable) to derive the invocation name; strip directory + extension and compare case-insensitive against `protect` / `unprotect`. Confirm this works under AOT — it should; `Environment.ProcessPath` is trim-safe.
