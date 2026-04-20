# protect / unprotect — Architecture Decision Record

**Date:** 2026-04-20
**Status:** Accepted
**Companion document:** [2026-04-20-protect-design.md](2026-04-20-protect-design.md)
**Context:** Designing `protect` and `unprotect`, paired tools for cross-platform encrypt-at-rest using native OS key-storage primitives. First tool in the v0.4.0 release (branch `release/v0.4.0`, branched off `release/v0.3.0` after qr landed).

---

## D1. Native OS secret stores only (Option A); bundled-age and hybrids rejected

**Context.** Three candidate approaches existed for the encryption key:
- **A. Native OS stores** — DPAPI / Keychain / libsecret.
- **B. Bundle `age`** — user manages an age identity file.
- **C. Hybrid** — age private key stored in OS secret store.

**Decision.** Option A only. v1 targets OS-native primitives, full stop.

**Rationale.** The headline pitch is "no key management for the user." Options B and C introduce user-facing key material (the age identity, even when wrapped). Option A keeps the key entirely inside the OS — the user never sees, exports, manages, or backs up anything. That matches the intent of DPAPI as a primitive and is the value proposition that's genuinely unfilled on all three platforms.

**Trade-offs accepted.** Files are not portable between machines or users. This is explicit in the design, documented in the help text and man page, and the platform marker byte in the file header means cross-platform moves fail with a specific "this was encrypted on {platform}; cannot decrypt here" error rather than silent mis-decryption. Users who need portable encryption have `age`, `gpg`, or `sops`; `protect` is not trying to compete in that space.

**Options considered.**
- **Option B (bundle age).** Rejected — adds a user-facing key file. If the user loses it, data is gone, and the "no key management" pitch breaks immediately.
- **Option C (hybrid).** Rejected for v1 as too complex for the first release of this tool. Could be revisited as a separate "portable protect" tool later if there's demand, but it would be a different tool with a different name.

---

## D2. Path Y: platform-appropriate primitives per platform; not a uniform cross-platform AEAD

**Context.** Inside Option A, a further choice existed:
- **Path X (uniform AEAD)**: all platforms use AES-256-GCM with a key stored in Credential Manager / Keychain / libsecret. Uniform file format, uniform code path.
- **Path Y (platform-appropriate)**: Windows uses raw DPAPI; Mac and Linux use AES-GCM with an OS-stored key.

**Decision.** Path Y.

**Rationale.** On Windows, `protect` is genuinely a DPAPI CLI — that's the gap we're filling and the pitch we're making. Under Path X, Windows would route through Credential Manager + AEAD, which is a) not DPAPI directly, b) introduces a synthetic 32-byte key we generate (vs DPAPI's OS-derived keyless-from-user-POV model), and c) stores data in two places (Cred vault + file) instead of one. That weakens the pitch materially.

On macOS and Linux, there is no DPAPI equivalent. The closest available primitive is "named key-value store" (Keychain items, libsecret items). We use that to hold a 32-byte AES key and do the crypto ourselves — this is structurally the best available option on those platforms, because neither OS exposes a "keyless encrypt this stream" primitive.

Path Y's honest answer is: Windows uses its DPAPI primitive directly; Mac and Linux use the closest-equivalent pattern their OS offers. The UX is consistent ("no key management"), the mechanisms differ because the OS primitives differ.

**Trade-offs accepted.** Two file-format payloads (DPAPI blob vs AEAD chunks) instead of one. Platform marker byte in the header distinguishes; decode fails helpfully if a file is read on the wrong platform. Testing is slightly more complex — platform-native backends require integration smoke tests on the corresponding CI runner. The uniform-format argument (easy cross-platform round-trip tests) is lost, but we mitigate by writing platform-independent tests against `NullSecretStore` + `AeadBackend` for the chunk/framing/AAD logic, which is where the non-trivial bugs live.

**Options considered.**
- **Path X (uniform AEAD).** Rejected — weakens the Windows DPAPI-CLI pitch, introduces "two places" and "synthetic key" concerns, and the uniformity benefit is smaller than it looked once we realised the platform-specific bug surface lives in the OS-store shell-outs (which differ anyway regardless of crypto layer).

---

## D3. Windows Credential Manager API for envvault via advapi32 classic P/Invoke, NOT WinRT PasswordVault

**Context.** Two Windows APIs exist for accessing Credential Manager:
- `Windows.Security.Credentials.PasswordVault` (WinRT) — modern, used by many doc examples.
- `advapi32.dll CredRead`/`CredWrite`/`CredDelete` (classic Win32) — used by git-credential-manager, AWS CLI, Azure CLI.

**Decision.** Classic Win32 P/Invoke via `advapi32.dll`, for both `WindowsCredentialManagerStore` (which `envvault` will consume) and any future Credential Manager interaction.

**Rationale.** WinRT `PasswordVault` **requires MSIX packaging or a package identity**. Unpackaged desktop apps throw "Class not registered" or similar runtime failures. Winix tools ship as plain desktop executables (AOT native, no packaging). WinRT would be an immediate dead-end. Classic Win32 Credential Management API has no identity requirement, works from any console app, and is what every established CLI tool on Windows uses for this purpose.

**Trade-offs accepted.** More verbose (P/Invoke structs, explicit memory management via `NativeMemory.Alloc` / `Free`, null-terminated UTF-16 conversions). Offset: well-documented on MSDN, stable since Windows 2000, AOT-clean (classic P/Invoke AOT has been solid since .NET 8).

**Options considered.**
- **WinRT `PasswordVault`.** Rejected — packaging requirement is incompatible with CLI distribution.
- **Windows.Security.Cryptography.KeyDerivation or similar.** Rejected — different abstraction, doesn't match "named credential storage" semantically.

(Note: `protect` on Windows does NOT use Credential Manager — it uses DPAPI directly. This ADR entry is about what `envvault` will do, and what `WindowsCredentialManagerStore` in the shared library uses, because the choice needs to be locked in when `SecretStore` is designed.)

---

## D4. Machine scope in v1 on Windows + macOS; Linux fails fast

**Context.** DPAPI on Windows supports both `CurrentUser` and `LocalMachine` scopes. macOS Keychain has user keychains and a System Keychain (accessible with sudo). Linux libsecret is session-oriented and has no equivalent machine-scope primitive natively — `systemd-creds` (systemd 250+) is the closest but has uneven distro adoption.

**Decision.** `--scope {user,machine}` flag in v1, default `user`. Supported combinations:
- Windows + user: DPAPI `CurrentUser`. ✓
- Windows + machine: DPAPI `LocalMachine`. ✓
- macOS + user: login Keychain. ✓
- macOS + machine: System Keychain (requires sudo). ✓
- Linux + user: libsecret. ✓
- Linux + machine: **fails fast** at ArgParser with `"Machine scope is not supported on Linux. Use user scope, or install systemd-creds."`

**Rationale.** Windows users who encrypt for service-account scenarios (a service running under a different user than the encryptor) genuinely need machine scope — that's a common dev/debug workflow. macOS System Keychain fills the same niche via the `sudo` + system-keychain path. Dropping these to achieve cross-platform uniformity would remove a real use case without gaining much (Linux users would still have user scope, which is what they already use on Linux today).

Linux's lack of a clean machine-scope primitive is an honest constraint. Rather than inventing one (e.g., storing a root-owned key file in `/var/lib/winix/`, which would be "us implementing machine scope by convention" and arguably worse than not offering it), we fail fast with a message that tells the user exactly what's happening and points at systemd-creds for the future case.

**Trade-offs accepted.** Asymmetric UX across platforms. Mitigated by:
- The `--scope` flag and its platform-specific behaviour are clearly documented in `--help`, the man page, and the README.
- The failure message on Linux + machine is specific, not a generic "unsupported option."
- File header reserves platform marker byte 0x21 for a future `LinuxSystemdCreds` backend, so v1 files are forward-compatible with a v2 that adds Linux machine scope.

**Options considered.**
- **User scope only in v1** — rejected. Windows + Mac users with legitimate service-account workflows shouldn't wait.
- **Linux fallback to file-based `/var/lib/winix/key`** — rejected. Not a native primitive; reinvents what systemd-creds will eventually solve correctly.
- **Support all platforms for machine scope with best-effort Linux** — rejected as too vague; the right-thing for Linux machine scope is systemd-creds, and we'll do it properly when we add it.

---

## D5. Unified chunked file format; no single-shot special case

**Context.** Two format strategies were possible: a) single-shot format for small files (IV | ciphertext | tag) with a separate chunked format for large ones, distinguished by a format-variant byte or by heuristic; b) always use chunked format, with a small file being exactly one chunk.

**Decision.** Always chunked. Every file is a stream of `is_final(1) | iv(12) | length(4) | ciphertext(length) | tag(16)` (AEAD path) or `length(4) | dpapi_blob(length)` with `is_final` embedded in the plaintext (DPAPI path). A small file is a one-chunk stream; a large file is many-chunk.

**Rationale.** Single codepath is simpler, has fewer test matrices, and removes an entire class of "which format is this?" bugs. The overhead for small files is negligible: for a file ≤ 64 KB plaintext, AEAD framing adds ~33 bytes (<0.1%). DPAPI adds ~5 bytes of our framing plus DPAPI's own internal overhead, which would exist in a single-shot format too.

Streaming also unlocks arbitrarily large files in v1 (terabyte files work with 64 KB + 64 KB memory footprint) without a "file too large" cap — which was an awkward v1 constraint in the original design and would have been a YAGNI complaint waiting to happen.

**Trade-offs accepted.** Per-chunk overhead for small files is non-zero. Not a real concern in practice — it's bytes, not a percentage that matters. Users who want zero-overhead can use `age` or `gpg` directly.

**Options considered.**
- **Separate single-shot format with a format-variant byte.** Rejected — extra complexity with no measurable benefit.
- **Header includes total chunk count.** Rejected — forces either two-pass encryption (know the size before writing the header) or rewriting the header at end-of-stream (breaks purely-streaming writers). Per-chunk `is_final` flag solves the EOF problem without either.

---

## D6. Round-trip verification by default, `--no-verify` opt-out; stricter than sops

**Context.** The closest precedent for `protect` is [sops](https://github.com/getsops/sops). sops supports `--in-place` with a temp-file-plus-atomic-rename pattern. **sops does NOT do round-trip verification.** Its reasoning: the AEAD primitive provides authenticated integrity, and any disk/memory corruption during encryption is caught at subsequent decrypt time via the authentication tag.

**Decision.** `protect` does round-trip verification by default (`--no-verify` to opt out). During encryption, incremental SHA-256 hashes the source bytes. After encryption, the output is stream-decrypted and a second incremental SHA-256 hashes the decrypted bytes. If they match, the encryption is validated. Mismatch → source preserved, output deleted, exit 126.

**Rationale.** AEAD authentication catches corruption *of the ciphertext*. It does NOT catch a bug in *our* code that produces a valid-but-wrong-plaintext AEAD blob — e.g., IV reuse, stream-offset bug, wrong key retrieved from SecretStore, wrong AAD construction. That bug class is narrow but catastrophic: the user has successfully encrypted garbage over their source, and they won't discover it until they try to decrypt (possibly much later, possibly after the source is deleted via `--rm`).

The cost is one extra decrypt pass on the output. For typical file sizes this is milliseconds. `--no-verify` is available for users who benchmark and want the speed (matches sops's behaviour if they prefer the precedent).

**Trade-offs accepted.** Doubles the crypto work per encryption. Mitigated by `--no-verify` opt-out and by the fact that typical file sizes (config files, API keys, small blobs) make the overhead invisible.

**Options considered.**
- **Match sops — no verification.** Defensible. Rejected in favour of defensive default for a security-critical tool where the failure mode of "silently corrupted" is much worse than a small latency cost. The `--no-verify` flag gives users who prefer sops's position an explicit opt-in.
- **Verify only under `--in-place`.** Rejected — the bug class we're defending against applies equally to non-in-place encryption. If the failure mode matters in one mode, it matters in both.

---

## D7. Two binaries (`protect` + `unprotect`), same library; argv[0] dispatch

**Context.** Three packaging options for the pair of commands:
- **a. Two `.csproj` files** producing `protect.exe` and `unprotect.exe`, both referencing `Winix.Protect`.
- **b. Single binary** (`protect.exe`) that responds to both invocations via symlink + argv[0] sniffing (busybox pattern).
- **c. Single binary with subcommands** (`protect encrypt FILE` / `protect decrypt FILE`).

**Decision.** Option (a) — two separate csprojs, both referencing the same library.

**Rationale.** Option (c) has worse UX — users expect `unprotect` as a distinct command, symmetrical with `protect`. Option (b) requires symlink creation at install time, which is incompatible with how winget, scoop, and `dotnet tool install` deliver binaries. Two csprojs is simple, no multi-call-binary complexity, and matches how every other Winix tool ships.

**Trade-offs accepted.** Slightly larger total download size (two nearly-identical executables). For AOT-native binaries of 1–2 MB each, this is ~2-4 MB total rather than ~1-2 MB. Acceptable.

**Options considered.**
- **Single binary with argv[0] symlink dispatch.** Rejected — doesn't fit winget/scoop install conventions.
- **`protect encrypt` / `protect decrypt` subcommands.** Rejected — worse UX for a symmetrical pair.

---

## D8. `Winix.SecretStore` extracted as shared library from day one

**Context.** `Winix.SecretStore` will be consumed by both `protect` (for Mac/Linux key storage) and `envvault` (for all platforms, KEY=VALUE namespace management). YAGNI would suggest inlining in `Winix.Protect` and extracting later when envvault lands.

**Decision.** Extract `Winix.SecretStore` from day one.

**Rationale.** Matches the precedent set by `Winix.QrCode` (extracted up front when `http --qr` was known-proximate), `Winix.FileWalk` (extracted for `files`/`treex`), `Winix.Codec` (extracted for `ids`/`digest`). The second consumer (`envvault`) is planned and proximate — it's the very next tool. Extracting later would mean a refactor pass when `envvault` starts, during which the `ISecretStore` API might get pulled in a direction optimised for envvault's needs and mismatched with what `protect` already expected. Better to design the API once, with both consumers in mind.

**Trade-offs accepted.** Slightly larger project graph (one extra csproj and one extra test csproj). Matches existing suite conventions; no new complexity.

**Options considered.**
- **Inline in `Winix.Protect`, extract when `envvault` lands.** Rejected — increases risk of API drift and forces a refactor when envvault work begins.
- **Two separate libraries (one for protect's use, one for envvault's).** Rejected — the abstraction is the same (named KEY=VALUE store with OS-specific backends); duplicating would be worse.

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|---|---|
| Portable encryption (cross-platform file moves) | Not a goal of the tool. Use `age` / `gpg` / `sops` for that workflow. |
| Named keys (`--key NAME`) | Single default key per scope in v1. Rotation and per-context keys become interesting in v2; not needed for the majority workflow. |
| Additional AAD (`--aad STRING`) | Header bytes are implicit AAD. User-supplied AAD is a v2 flag if context-binding is requested. |
| Configurable chunk size | Hard-coded 64 KB in v1. `--chunk-size N` in v2 if anyone measures and needs it. |
| Linux machine scope | No clean native primitive yet; systemd-creds adoption uneven across distros. Reserved platform marker 0x21 for v2. |
| In-place on symlinks | Path resolution + atomic rename interaction has corner cases; v1 refuses explicitly. v2 can address once the design is thought through. |
| Compression integration | Users compose with `squeeze`. Built-in compression would create format-variant complexity. |
| Password-based fallback | OS store is the value proposition. Allowing a password fallback would undermine the design. |
| Batch mode (multi-file per invocation) | Shell `for` loop covers it. Reject scope creep. |
| Keychain entry cleanup on uninstall | Out of scope for a tool; installers remove binaries, not user data. Users who want to forget their `protect` key can call `security delete-generic-password` / `secret-tool clear` / `cmdkey /delete` manually (documented in man page). |
