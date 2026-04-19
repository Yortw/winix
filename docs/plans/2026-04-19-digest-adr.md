# ADR: digest Tool Design Decisions

**Date:** 2026-04-19
**Status:** Proposed
**Context:** Design for `digest`, a cryptographic hashing and HMAC CLI for the Winix suite.
**Related:** [digest design doc](2026-04-19-digest-design.md)

---

## 1. Four HMAC Key Input Mechanisms — All First-Class, with Warnings for the Unsafe One

**Context:** HMAC CLIs routinely get key handling wrong. Most accept `--key <literal>`, which leaks via `ps`, shell history, `/proc/*/cmdline`, systemd journal, audit logs, and CI platform logs. `openssl dgst -hmac <key>` has the same issue. The result: users who don't know the leak paths pick the easy flag and silently publish their HMAC keys to logs.

**Decision:** Expose four distinct key-source flags, each mapping to a different threat model:

- **`--key-env VAR`** — reads from environment variable. Value not visible in `ps`, but visible in `/proc/*/environ` to same-UID processes. Standard for CI secrets.
- **`--key-file PATH`** — reads from filesystem. Value never in process memory of other processes. Subject to filesystem backup / permission issues. Standard for long-lived service secrets.
- **`--key-stdin`** — reads from stdin. Ephemeral, gone after read. Composes with secret stores (`pass show | digest --key-stdin`, `age --decrypt | digest --key-stdin`). Standard for interactive + secret-store workflows.
- **`--key KEY`** — literal on command line. Leaks to `ps`, shell history, logs. **Emits stderr warning on every invocation.**

**Rationale:** Users need all four because their threat models differ. A CI pipeline has the secret in an env var already — `--key-env` is the right shape. A machine-local service has the secret in a chmod-600 file — `--key-file`. An interactive user pulling from a password store pipes via stdin. The unsafe `--key` stays for the "quick throwaway demo" case, but warns so unawareness doesn't become complacency. The warning has no suppression flag — the warning *is* the feature.

**Trade-offs Accepted:** Bigger flag surface than `openssl dgst` has. Users must understand the threat model of their chosen mechanism. Documented in the README with explicit comparisons.

**Options Considered:**
- **Only `--key-stdin`.** Forces secret-store composition, which is correct but makes the common "env var in CI" case awkward (`printenv API_SECRET | digest --key-stdin` — noisy and error-prone if the env var has trailing whitespace).
- **Only `--key` with no warning.** What `openssl dgst` does. Rejected — perpetuates the leaky default.
- **`--key` + a suppressible warning.** Rejected — the warning is the point. Making it suppressible lets scripts silence it without fixing the underlying problem.

---

## 2. Legacy Hashes (MD5, SHA-1) Included with Stderr Warnings

**Context:** MD5 is cryptographically broken (collision attacks from 2004 onward). SHA-1 is broken for collision resistance (SHAttered, 2017). Should `digest` support them?

**Decision:** Support MD5, SHA-1, HMAC-MD5, and HMAC-SHA-1. Emit a stderr warning whenever any is used:

```
digest: warning: MD5 is cryptographically broken; do not use for security-sensitive purposes.
digest: warning: SHA-1 is broken for collision resistance; HMAC-SHA-1 is still acceptable for signing but prefer HMAC-SHA-256 for new systems.
```

**Rationale:** Legitimate use cases exist:
- Verifying downloads from legacy sources that publish only MD5/SHA-1.
- Interoperating with older systems (AWS Signature v4 uses HMAC-SHA-1).
- Checksumming non-adversarial integrity (package file hashes where the server already signs with SHA-256).

Excluding them entirely would force users back to `openssl dgst` or `md5sum` for these cases, defeating the "one cross-platform tool" promise. The stderr warning makes the security context unavoidable: a user who needs the hash sees the warning every time and can't cargo-cult MD5 into a new security-sensitive context without noticing.

**Trade-offs Accepted:** Every scripted `digest --sha1 …` invocation emits a warning line. Users may be tempted to `2>/dev/null` it, which defeats the purpose. Acceptable — we can't stop users from silencing warnings, but making the warning the visible default puts the onus on the user.

**Options Considered:**
- **Exclude MD5 and SHA-1 entirely.** Rejected — breaks real users for no safety gain (HMAC constructions aren't affected the way bare hashes are).
- **Include silently.** Rejected — normalises broken crypto.
- **Include behind a `--allow-legacy` opt-in flag.** Rejected — adds ceremony for interoperability tasks that are the whole reason users need these.

---

## 3. BLAKE2b via `Blake2Fast` NuGet Dependency (not in-house)

**Context:** BLAKE2b isn't in the .NET BCL. We need an implementation. Three options: add a dependency, implement ourselves, or drop BLAKE2b from v1.

**Decision:** Add `Blake2Fast.Blake2b` as a NuGet dependency. First external crypto dep in Winix.

**Rationale:** `Blake2Fast` is ~2 KB compiled, pure-managed, AOT-friendly, MIT-licensed, actively maintained, and has its correctness verified against RFC 7693 test vectors in its own test suite. Implementing BLAKE2b ourselves in `Winix.Codec` would be ~150 lines (the algorithm is well-specified) but the maintenance cost isn't zero — any future bug fixes or performance improvements in the upstream implementation would require manual tracking.

Dropping BLAKE2b from v1 would also be defensible, but it's genuinely popular for content-addressed storage (Argon2 family, IPFS, git's proposed next-gen hash). Having it available means `digest --blake2b file` just works.

**Trade-offs Accepted:** First external crypto dep in Winix. Adds ~2 KB to the AOT binary (negligible vs the hundreds of KB from BCL crypto). The precedent is important: future tools may want to add other crypto deps (e.g., age-encryption for the `protect` tool). Each such dep gets its own ADR.

**Options Considered:**
- **Implement BLAKE2b ourselves in Winix.Codec.** Rejected — maintenance cost over time exceeds the benefit of zero deps. Reviewed as a fallback if `Blake2Fast` ever becomes unmaintained or develops AOT regressions.
- **Drop BLAKE2b from v1, plan for v2.** Rejected — it's a recognised modern hash and losing it weakens the "supports modern hashes" positioning.
- **Wait for BCL support.** .NET has been slow to add BLAKE-family hashes. Unlikely to change soon.

---

## 4. Strip One Trailing Newline on `--key-file` and `--key-stdin` by Default

**Context:** Shell commands that emit keys almost always append a trailing newline: `echo`, `printf '%s\n'`, `cat`, `openssl rand -base64`, `pass show`, `age-keygen`. If `digest` reads the file/stdin as-is, users see HMAC mismatches because the trailing `\n` is an invisible extra byte.

**Decision:** Default behaviour: strip exactly one trailing `\n` or `\r\n` from key bytes read via `--key-file` or `--key-stdin`. Multi-line content retains internal newlines; only the final one is removed. `--key-raw` preserves bytes exactly.

**Rationale:** Matches the `clip` tool's asymmetric newline behaviour (which was itself chosen to match `$(…)` shell-substitution semantics). Users who `echo "secret" > keyfile` get a 6-byte key, which is what they visually see. Users who genuinely want the trailing newline (some HMAC test vectors include it intentionally) use `--key-raw`.

Aligning `--key-file` and `--key-stdin` on the same rule is deliberate. Earlier drafts considered stripping only `--key-file` (on the theory that stdin users pipe from explicit commands that handle whitespace). But the `--key` literal uses no stripping (it's the raw string), `--key-env` uses no stripping, and having `--key-stdin` silently differ from `--key-file` would surprise users switching between `pass show | digest --key-stdin` and `pass show > keyfile; digest --key-file keyfile`.

**Trade-offs Accepted:** Users with keys that legitimately end in `\n` must use `--key-raw`. Acceptable — this is rare (keys aren't typically newline-terminated at rest; the newline is a shell-echo artefact).

**Options Considered:**
- **Never strip.** Rejected — causes invisible-whitespace HMAC mismatches that are mystifying to debug.
- **Strip all trailing whitespace.** Rejected — too aggressive; HMAC keys with intentional trailing whitespace would be corrupted.
- **Strip only on `--key-file`, not `--key-stdin`.** Rejected — inconsistency between two related flags.

---

## 5. All-or-Nothing Multi-File Validation (Not sha256sum's Partial-Output Behaviour)

**Context:** `sha256sum file1 file2 file3 nonexistent file4` processes files in order, emitting a hash line for `file1`, `file2`, `file3`, then an error for `nonexistent`, then a hash line for `file4`. Partial output followed by a mid-stream error.

**Decision:** In `digest` multi-file mode, validate all positional arguments up front (each must satisfy `File.Exists`). If any is missing, exit 125 *before* producing any output.

**Rationale:** Partial output followed by an error is confusing in piped contexts. `digest *.log | xargs process-hash` that half-completes leaves the downstream in an ambiguous state — did the hashes that did emit actually correspond to reliable inputs? Up-front validation makes the contract clean: either we produce hashes for all requested files, or we produce none and report the error cleanly.

The all-or-nothing cost is a small latency bump on cold-start when processing thousands of files (the `File.Exists` check is tens of microseconds each), which is imperceptible.

**Trade-offs Accepted:** Slight divergence from `sha256sum` behaviour. Users expecting literal `sha256sum` semantics may be surprised. Documented in the README's "differences from sha256sum" section.

**Options Considered:**
- **Match sha256sum exactly.** Rejected — the pipeline ambiguity isn't worth backward compatibility with an inherited design quirk.
- **`--stop-on-error` flag defaulting to on.** Rejected — the up-front validation is strictly safer and adds no meaningful performance cost.

---

## 6. Multi-File Output Uses the `*` (Binary-Mode) Marker, Not Two Spaces

**Context:** GNU `sha256sum` has two output-line forms, distinguished by a marker between the hash and the filename:
- `<hash>  <filename>` (two spaces) — text mode; the hash was computed after CR/LF → LF translation.
- `<hash> *<filename>` (space + asterisk) — binary mode; the hash was computed over raw bytes.

On Unix text and binary modes produce identical hashes (no CR/LF translation happens), so the marker is historical/cosmetic there. On Windows, text mode normalises CR/LF before hashing, which diverges from binary mode.

**Decision:** Use the `*` binary-mode marker in `digest`'s multi-file output: `<hash> *<filename>`.

**Rationale:** `digest` always reads files as raw bytes — we never do CR/LF translation. The `*` marker honestly describes this. Using two spaces (text mode) while doing binary-mode hashing would be a mis-signal: a user inspecting the SHA256SUMS file couldn't tell from the marker whether this hash came from text or binary mode.

Cross-platform correctness also benefits. A Windows user generating a SHA256SUMS with `digest` and verifying it on Linux with GNU `sha256sum -c SHA256SUMS` gets matching behaviour because both sides agree it's binary mode. If we used two spaces (text mode) on Windows, GNU `sha256sum -c` would try to CR/LF-translate the referenced file during verify and get a different hash on files containing bytes that look like CR/LF.

`sha256sum -c` accepts both markers during verification, so existing verification flows aren't broken by the choice.

**Trade-offs Accepted:** Users accustomed to GNU `sha256sum`'s Unix default output (two spaces) will see an unfamiliar marker in `digest`'s output. Acceptable — the marker is documented in the README and it's the correct one for our behaviour.

**Options Considered:**
- **Two spaces (GNU default on Unix).** Rejected — misrepresents our binary-mode behaviour.
- **Two spaces with a README note explaining we always binary-hash.** Rejected — the marker is right there in every output line; documentation can't overcome a visible mis-signal.
- **Add a `--text-mode` flag for CR/LF normalisation.** Rejected — text-mode hashing is nearly always wrong in 2026 (modern cross-platform workflows treat files as bytes), and adding the flag invites users to reach for it without understanding the consequences. Not worth the ceremony.

---

## 7. Verify Mode Uses Exit Code 1 for Mismatch (Not 125)

**Context:** `digest --verify <expected> file` compares. Mismatch is a normal outcome — the tool worked correctly, the answer was "no." What exit code?

**Decision:** Exit 1 for mismatch, exit 0 for match. Usage errors (bad flags, `--verify` with multi-file) stay at 125.

**Rationale:** Matches the POSIX convention used by `grep`, `diff`, `cmp`, `test`: the tool ran successfully, the boolean answer was false. Keeping 125 for "the tool couldn't even run" (bad flags, missing files, unknown algorithms) preserves the distinction between "this tool was used incorrectly" and "this tool worked, here's the result". Scripts doing `digest --verify ... && echo OK` compose naturally.

**Trade-offs Accepted:** Users who conflate "exit non-zero" with "tool crashed" will need to learn the distinction. Acceptable — this is a well-established Unix convention, and the Winix README standard page covers exit codes.

**Options Considered:**
- **Exit 125 for mismatch.** Rejected — conflates user error with verification result.
- **Exit 2 for mismatch.** Rejected — `diff` uses 2 for "trouble" (file not found, etc.), so 2 is usually a worse-than-mismatch signal. Stay with 1.

---

## 8. Encrypted-at-Rest Key Files: Deferred to a Separate `protect`/`unprotect` Tool

**Context:** `--key-file` reads an unencrypted file. The question arose: should `digest` support decryption (DPAPI-unprotect, age-decrypt, GPG-decrypt, Keychain-fetch) natively? Or should that be a separate tool?

**Decision:** No encryption built into `digest`. Users compose via `--key-stdin` with their existing secret store: `age --decrypt key.age | digest --hmac sha256 --key-stdin`. A future Winix `protect`/`unprotect` tool (now on the Tier 1 list) will unify this cross-platform.

**Rationale:** Each OS has a different native secret-storage primitive (DPAPI, Keychain, Secret Service). Cross-platform tools (age, GPG, `pass`) solve this at the application layer with their own key models. Baking any of these into `digest` either picks winners or turns digest into a Swiss Army knife. The Unix way is orthogonal composable tools: `digest` reads key bytes; something else provides the bytes.

The composition pattern already works today via `--key-stdin`. Users of `pass`, `age`, `security find-generic-password`, or `secret-tool` all get encrypted-at-rest keys without any `digest`-specific work. Users of `--key-file` are choosing unencrypted storage explicitly (often for automation where interactive decryption isn't viable).

A future `protect`/`unprotect` Winix tool would provide a unified `unprotect < secret.prot | digest --key-stdin` pattern that works cross-platform, wrapping DPAPI on Windows and shell-out to `security`/`secret-tool` on macOS/Linux (or optionally bundling age for a cross-platform implementation). That's a separate design with its own brainstorm.

**Trade-offs Accepted:** `--key-file` can mislead users into thinking it's safe regardless of file protection. Mitigated by: (a) the Unix permission warning for group/other-readable files, (b) the README section on encrypted-at-rest patterns with explicit examples, (c) the future `protect`/`unprotect` tool giving a clean upgrade path.

**Options Considered:**
- **Build DPAPI unprotect into `--key-file`** (Windows-only). Rejected — picks winners, inconsistent cross-platform behaviour.
- **Build age-decrypt into `--key-file`.** Rejected — forces an age key management story on every user; scope creep.
- **Detect encrypted files (GPG magic bytes, age header) and warn.** Considered as a nice-to-have. Deferred — small scope creep and the composition pattern documentation covers the education angle.

---

## 9. File/String Auto-Detection with Explicit Override Flags

**Context:** `digest hello` is ambiguous. Is "hello" a filename or a literal string? `sha256sum` sidesteps this by only accepting files (or `-` for stdin). Our interface supports both.

**Decision:** Auto-detect by `File.Exists()`: if the positional arg names an existing file, treat as file input; otherwise, treat as literal string. Provide `--string` and `--file` explicit override flags.

**Rationale:** Auto-detect covers the overwhelmingly common cases cleanly: `digest ./downloaded.iso` hashes the file; `digest "hello"` hashes the string. The override flags handle edge cases (a file that looks like a sentence, a string that happens to match an existing filename) without ceremony for the common path.

**Trade-offs Accepted:** `digest file.txt` where `file.txt` doesn't exist will silently hash the literal string `"file.txt"`. This is a potential surprise. Mitigation: the README calls this out explicitly, and `--file` forces file-mode with a clear error if the file is missing. Scripts that want deterministic behaviour should use `--file` or `--string` explicitly.

**Options Considered:**
- **No auto-detect; always literal string unless `--file` is used.** Rejected — breaks the intuitive `digest somefile` invocation that mirrors `sha256sum somefile`.
- **No auto-detect; always file unless `--string` is used.** Rejected — breaks the `digest "quick test string"` invocation that's a common ad-hoc use.
- **No auto-detect; require explicit `--string` or `--file` always.** Rejected — too much ceremony for the common cases.

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|---|---|
| BLAKE3 support | No AOT-vetted .NET implementation as of 2026-04-19. |
| sha256sum `--check` checkfile verification | `--verify <expected>` covers single-hash case; multi-file checkfile is a bigger v2 design (format parsing, per-file status, failure-mode modelling). |
| CRC32 / xxHash / non-cryptographic hashes | Different market (fast integrity vs security). Separate tool if ever. |
| Directory hashing / content-addressed storage | Larger design; belongs in its own tool. |
| Native DPAPI / Keychain / Secret Service integration | Deferred to future `protect`/`unprotect` Winix tool (now Tier 1). |
| `--recursive` directory traversal | Conflicts with explicit-list semantics; v2 if demand appears. |
| `--output <file>` explicit output redirection | Shell `>` works today; explicit flag only adds value for weird shell-quoting cases. v2 if needed. |
