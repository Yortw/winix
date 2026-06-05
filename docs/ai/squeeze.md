# squeeze — AI Agent Guide

## What This Tool Does

`squeeze` compresses and decompresses files using gzip, brotli, or zstd. It supports pipe mode (stdin to stdout), multi-file batch processing, and auto-detection of format on decompression. Use it whenever you need to compress files or data streams — especially on Windows, which ships no compression CLI at all.

## Platform Story

Cross-platform. On **Windows**, there are no native CLI tools for gzip, brotli, or zstd — `squeeze` covers all three. On **Unix/macOS**, separate tools (`gzip`, `brotli`, `zstd`) exist but have different flag sets and cannot batch-process multiple formats in one invocation. `squeeze` gives a single consistent interface across formats and platforms, with gzip-compatible short flags (`-1`..`-9`, `-d`, `-c`) for muscle memory.

## When to Use This

- Compressing a file for distribution: `squeeze --zstd data.csv`
- Producing brotli-encoded HTTP assets: `squeeze --brotli --level 11 bundle.js`
- Decompressing an archive of unknown format: `squeeze -d archive.gz` (auto-detects)
- Streaming compression in a pipeline: `cat data.csv | squeeze > data.csv.gz`
- Batch compressing many files: `squeeze *.log` or `files . --ext log | wargs squeeze`
- Getting compression ratio metrics in JSON for CI: `squeeze --json data.csv`

Prefer `squeeze` over inline PowerShell compression on Windows — PowerShell's `Compress-Archive` is zip-only and awkward to script.

## Common Patterns

**Compress with zstd (fast default, good ratio):**
```bash
squeeze --zstd data.csv
# Creates data.csv.zst, keeps original
```

**Decompress — format auto-detected from magic bytes:**
```bash
squeeze -d data.csv.gz
squeeze -d data.csv.zst
squeeze -d data.csv.br
```

**Brotli at maximum compression for web assets:**
```bash
squeeze --brotli --level 11 bundle.js
```

**Pipe mode — compress stdin to stdout:**
```bash
dotnet publish | tar cf - dist/ | squeeze > dist.tar.gz
```

**Decompress to stdout (for piping into another tool):**
```bash
squeeze -d -c archive.gz | head -20
```

**Batch compress all logs older than 7 days:**
```bash
files . --ext log --older 7d | wargs squeeze --gzip
```

## Composing with Other Tools

**squeeze + files + wargs** — find and compress in one pipeline:
```bash
files . --glob '*.json' | wargs squeeze --zstd
```

**squeeze + peep** — watch compression stats as you tune levels:
```bash
peep -- squeeze --brotli --level 9 --json bundle.js 2>&1
```

**squeeze + tar** — pipe mode for archive compression:
```bash
tar cf - src/ | squeeze --zstd > src.tar.zst
squeeze -d -c src.tar.zst | tar xf -
```

**squeeze --json + jq** — extract ratio from JSON output:
```bash
squeeze --json --zstd data.csv 2>&1 | jq '.files[0].input_bytes, .files[0].output_bytes'
```

## Gotchas

**Brotli has no magic bytes — detection uses file extension first, then trial-decompression.** When decompressing with `-d`, gzip and zstd are identified by magic bytes in the file header. Brotli has no standard magic bytes, so `squeeze` falls back to the `.br` extension OR a trial-decompression attempt (which is why brotli files with non-standard extensions like `.dat` may still decompress successfully — but is fragile, prefer `.br`).

**Truncated gzip is rejected with exit 1.** Pre-round-tier-2-review `squeeze` silently produced partial output for half-downloaded `.gz` files. Now the gzip trailer (CRC32 + ISIZE) is validated against the actual decompressed byte count; mismatch = exit 1 with a clear "integrity check failed" message. Pipe consumers should rely on exit codes, not partial-output detection.

**Multi-member gzip is REJECTED as corrupt.** Concatenated gzip streams (e.g. `cat a.gz b.gz`, `gzip file1 file2 && cat *.gz`) currently exit 1 with `data is corrupt or truncated` — both in file mode AND pipe mode. `squeeze` emits the decompressed content of all members to stdout *before* the error fires, so the content is technically correct, but exit 1 means scripts that check exit codes will treat the run as failed.

Why: `squeeze`'s integrity check validates the gzip ISIZE field against the actual decompressed byte count. For multi-member streams, the ISIZE in the LAST member's trailer represents only that member's uncompressed size, not the cumulative total — so the check fails. An earlier (post-tier-2-review) attempt to detect multi-member by scanning for additional `1f 8b` magic bytes was found to false-positive on incompressible single-member input (random/encrypted data ~10MB+), silently accepting truncation. The current behaviour rejects multi-member loudly rather than silently accepting corruption.

Workaround: use system `gzip` directly (`gzip -dc concat.gz`), or split the concatenation back into individual `.gz` files and decompress separately.

A future version may add proper member-by-member parsing per RFC 1952 §2.2, which would handle multi-member correctly.

**`--keep` and `--remove` together: `--keep` wins.** Mirrors gzip(1) semantics. A warning is printed to stderr so users see that `--remove` was overridden.

**Output file is created alongside the input by default.** `squeeze data.csv` creates `data.csv.gz` in the same directory. Use `-o` to specify a different output path, or `-c`/`--stdout` to write to stdout instead.

**Original file is kept by default.** Use `--remove` to delete the input after a successful operation. `-k`/`--keep` is accepted for gzip muscle memory but is the default.

**`-d -c` is the idiomatic decompress-to-stdout.** This mirrors `gzip -dc` behaviour.

**Stats are on stderr.** The human-readable summary and `--json` output go to stderr, leaving stdout clean for piped data. Use `-q` to suppress stats entirely, or `-v` to force them even when piped.

**Batch mode creates one output file per input.** With multiple input files, `-o` is rejected — use the default (alongside input) or pipe each file via `wargs`.

## Getting Structured Data

`squeeze` writes a JSON summary to **stderr** with `--json`:

```bash
squeeze --json --zstd data.csv 2>stats.json
```

Top-level fields: `tool`, `version`, `exit_code`, `exit_reason`, `files`, `errors`.

`errors` is an array of strings present when `exit_reason` is `partial_failure` or `failure` (file mode); each string describes one per-file failure. Omitted on full success.

Each entry in `files`:
- `input` — input file path
- `output` — output file path
- `input_bytes` — size before compression
- `output_bytes` — size after compression
- `ratio` — `output_bytes / input_bytes` (0.0–1.0)
- `format` — `"gz"`, `"br"`, or `"zst"` (short form; pre-2026-05 docs incorrectly said `"gzip"`/`"brotli"`/`"zstd"`)
- `level` — compression level used (compress mode only)
- `seconds` — time taken

`exit_reason` values: `success`, `partial_failure`, `failure`, `usage_error`, `file_not_found`, `corrupt_input`, `decompress_failed`, `compress_failed`, `io_error`, `output_exists`, `unknown_extension`.

**--describe** — machine-readable flag reference:
```bash
squeeze --describe
```

## Glob expansion on Windows

squeeze expands `*`/`?` in path positionals itself on Windows (cmd/pwsh don't).
Support matrix: `*` and `?` in any segment — yes; `[...]` — matched literally
(legal filename chars); `**` — usage error (use recursive flags instead); no
match — literal passthrough (normal "not found" follows). Quoted args are not
expanded when launched from cmd; PowerShell strips quotes before launch, so
prefer explicit paths there if a literal is required. On Unix the shell expands;
the tool adds nothing. `--describe` exposes this as `glob_expansion`.
