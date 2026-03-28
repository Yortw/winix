# ADR: squeeze — Multi-format Compression

**Date:** 2026-03-29
**Status:** Accepted
**Related design:** `2026-03-29-squeeze-design.md`

---

## 1. Keep original file by default, accept -k as no-op

**Context:** gzip deletes the input file by default. This is the #1 complaint about gzip and a frequent source of data loss.

**Decision:** squeeze keeps the original by default. `--remove` explicitly opts into deletion. `-k`/`--keep` accepted as a no-op for gzip compatibility.

**Rationale:** Safer default. Scripts that already include `-k` (many do defensively) work unchanged. Scripts that rely on delete-after-compress must add `--remove` — but those are rarer and the behaviour is intentionally opt-in.

**Trade-offs Accepted:** Not a drop-in replacement for gzip in scripts that depend on deletion. Mitigated by clear documentation.

## 2. gzip as default format

**Context:** squeeze supports gzip, brotli, and zstd. One must be the default when no format flag is given.

**Decision:** gzip is the default.

**Rationale:** Universal decompressibility. Every system has gunzip. Scripts that produce `.gz` files know the output can be consumed anywhere. Zstd is better but not yet ubiquitous.

**Options Considered:**
- *zstd:* rejected — not universally installed, would surprise users expecting gzip behaviour.
- *Infer from extension:* deferred — nice quality-of-life feature, can be added later without breaking anything.

## 3. Magic bytes first, extension fallback for format detection

**Context:** When decompressing, squeeze needs to identify the format. Could use file extension, magic bytes, or both.

**Decision:** Magic bytes first (ground truth of what the data is), extension as fallback for ambiguous cases (mainly brotli, which has no magic bytes).

**Rationale:** Magic bytes are authoritative — a file named `.gz` could be mislabelled. Extension is a hint, not proof. This order handles pipe mode (no extension) gracefully for gzip and zstd.

**Trade-offs Accepted:** Brotli in pipe mode without `-b` flag may fail if auto-detection can't identify it. Acceptable — brotli pipes are rare and the flag is simple.

## 4. Exit codes match gzip (1 operational, 2 usage)

**Context:** Per Winix conventions, tools replacing existing utilities match the original's exit codes.

**Decision:** Exit 1 for operational errors, exit 2 for usage errors. Matches gzip.

**Rationale:** CI scripts checking `$?` against gzip exit codes continue to work.

## 5. Interactive stats with auto-detection, not silent by default

**Context:** gzip is silent on success. This is the Unix convention but misses an opportunity.

**Decision:** Show brief stats on interactive terminals, suppress when piped. `--verbose`/`--quiet` override.

**Rationale:** Same auto-detection pattern as timeit's colour handling. Interactive users get useful feedback (ratio, time). Scripts get silence. No flags needed for common cases.

**Options Considered:**
- *Silent always (like gzip):* rejected — misses the "better than the original" opportunity.
- *Stats always:* rejected — would pollute piped output.

## 6. ZstdSharp.Port for zstd support

**Context:** .NET has no built-in zstd. Options are ZstdSharp (P/Invoke wrapper around C libzstd) or ZstdSharp.Port (pure managed C# port).

**Decision:** Use ZstdSharp.Port — fully managed, no native dependencies, AOT-compatible.

**Rationale:** AOT compatibility is non-negotiable for Winix. Pure managed avoids platform-specific native library bundling. Performance is adequate for a CLI tool (not a high-throughput server).

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|-------|-------------|
| Infer format from output extension | Nice UX but not essential for v1, easy to add later |
| Progress bar for large files | Stats after completion are sufficient for v1 |
| Recursive directory compression | Shell globs and xargs cover this |
| zlib as output format | Niche use case, no demand |
| `--rsyncable` | gzip-specific optimisation, niche |
