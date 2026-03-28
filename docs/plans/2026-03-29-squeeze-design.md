# squeeze — Multi-format Compression

**Date:** 2026-03-29
**Status:** Design approved
**Project:** Winix (`D:\projects\winix`)
**Conventions:** `2026-03-29-winix-cli-conventions.md`

## Purpose

`squeeze` compresses and decompresses files using gzip, brotli, or zstd. Single tool, multiple formats. Replaces `gzip` with better defaults and adds brotli/zstd support that has no easy CLI equivalent on any platform.

## Usage

```
squeeze [options] [file...]

Options:
  -d, --decompress    Decompress (auto-detects format)
  --brotli, -b        Use brotli format
  --zstd, -z          Use zstd format
  --level N           Compression level (format-specific range)
  -1..-9              Compression level shortcut
  -c, --stdout        Write to stdout
  -o, --output FILE   Output file (single input only)
  -f, --force         Overwrite existing output files
  --remove            Delete input file after success
  --verbose, -v       Show stats even when piped
  --quiet, -q         Suppress stats even on terminal
  --json              JSON output
  --no-color          Disable colored output
  --color             Force colored output
  --version           Show version
  -h, --help          Show help

Compatibility:
  These flags match gzip for muscle memory:
  -d                  Same as --decompress
  -c                  Same as --stdout
  -k                  Accepted (keep is default, no-op)
  -1..-9              Same as --level 1..9
  -v                  Same as --verbose
  -f                  Same as --force

Exit Codes:
  0    Success
  1    Compression/decompression error (corrupt input, I/O failure)
  2    Usage error (bad arguments, conflicting flags)
```

## Modes

### File mode

```bash
squeeze file.csv              # → file.csv.gz (gzip default, keep original)
squeeze -b file.csv           # → file.csv.br (brotli)
squeeze -z file.csv           # → file.csv.zst (zstd)
squeeze -d file.csv.gz        # → file.csv (auto-detect format)
squeeze file1.csv file2.csv   # → file1.csv.gz file2.csv.gz
```

### Pipe mode

Auto-detected when stdin is redirected (`Console.IsInputRedirected`):

```bash
cat file.csv | squeeze > file.csv.gz
cat file.csv.gz | squeeze -d > file.csv
```

### Stdout mode

```bash
squeeze -c file.csv > somewhere.gz
squeeze -c -d file.csv.gz > file.csv
```

### Custom output

```bash
squeeze -o archive.gz file.csv
squeeze -o - file.csv          # same as -c (stdout)
```

`-o` with multiple input files is an error (exit 2).

## Compression Formats

### Format selection (compression)

| Flag | Format | Extension | Default level | Level range |
|------|--------|-----------|---------------|-------------|
| *(default)* | gzip | `.gz` | 6 | 1-9 |
| `--brotli`, `-b` | brotli | `.br` | 6 | 0-11 |
| `--zstd`, `-z` | zstd | `.zst` | 3 | 1-22 |

### Compression level

- `-1` through `-9`: shortcuts, work for all formats. Map directly to `--level 1` through `--level 9`.
- `--level N`: full native range. Out-of-range for the chosen format is an error (exit 2).
- Default levels chosen for balanced speed/ratio per format.

### Format detection (decompression)

Priority order:
1. **Magic bytes** — gzip: `1f 8b`, zstd: `28 b5 2f fd`. Definitive when present.
2. **Extension hint** — if magic bytes don't match known formats, check file extension (`.gz`, `.br`, `.zst`).
3. **Try brotli** — brotli has no magic bytes. If magic bytes and extension don't identify the format, attempt brotli decompression.
4. **Try raw deflate** — final fallback. Attempt raw deflate decompression.
5. **Error** — if all detection fails, exit 1 with "unrecognised format" error.

For pipe mode (no extension available), gzip and zstd are detected by magic bytes. Brotli pipes may need an explicit `-b` flag if auto-detection fails.

Explicit format flags (`-b`, `-z`) during decompression override auto-detection — the specified format is used directly, skipping magic bytes and extension checks.

Raw deflate is supported for decompression only — not offered as a compression output format.

## File Handling

### Output naming

**Compression:** append format extension.
- `file.csv` → `file.csv.gz` / `file.csv.br` / `file.csv.zst`

**Decompression:** strip known extension.
- `file.csv.gz` → `file.csv`
- `file.csv.br` → `file.csv`
- `file.csv.zst` → `file.csv`
- Unknown extension → error: "can't determine output name, use -o" (exit 1)

### Overwrite protection

If the output file already exists, error with exit 1. `--force`/`-f` overrides.

### Input file preservation

- **Default:** keep original file (safer than gzip's default).
- `--remove`: delete input file after successful compress/decompress.
- `-k`/`--keep`: accepted as a no-op for gzip compatibility (keep is already default).

### Partial output cleanup

If compression or decompression fails mid-stream (corrupt input, I/O error), delete the partial output file. A truncated `.gz` is worse than no file.

## Output and Stats

### Interactive stats

When stderr is a terminal, show one line per file after completion:

```
file.csv: 1.0 MB → 524 KB (50.0%, gz, 0.12s)
```

Format: `<filename>: <input size> → <output size> (<ratio>%, <format>, <time>)`

Multiple files:
```
file1.csv: 1.0 MB → 524 KB (50.0%, gz, 0.12s)
file2.log: 4.2 MB → 890 KB (79.3%, gz, 0.45s)
```

- Suppressed when stderr is piped.
- `--quiet`/`-q` suppresses even on terminal.
- `--verbose`/`-v` forces stats even when piped.

### Colour

Compression ratio in green when good (> 50% reduction), normal otherwise. Filenames in dim. Follows standard Winix colour resolution precedence (explicit flag > `NO_COLOR` > auto-detect).

### JSON output

Written to stderr (compressed data still flows to stdout/file as normal).

Success:
```json
{"tool":"squeeze","version":"0.1.0","exit_code":0,"exit_reason":"success","files":[{"input":"file.csv","output":"file.csv.gz","input_bytes":1048576,"output_bytes":524288,"ratio":0.500,"format":"gz","seconds":0.120}]}
```

Multiple files:
```json
{"tool":"squeeze","version":"0.1.0","exit_code":0,"exit_reason":"success","files":[{"input":"file1.csv","output":"file1.csv.gz","input_bytes":1048576,"output_bytes":524288,"ratio":0.500,"format":"gz","seconds":0.120},{"input":"file2.log","output":"file2.log.gz","input_bytes":4404019,"output_bytes":911360,"ratio":0.793,"format":"gz","seconds":0.450}]}
```

Error:
```json
{"tool":"squeeze","version":"0.1.0","exit_code":1,"exit_reason":"corrupt_input"}
```

Pipe mode (no filename):
```json
{"tool":"squeeze","version":"0.1.0","exit_code":0,"exit_reason":"success","files":[{"input":"<stdin>","output":"<stdout>","input_bytes":1048576,"output_bytes":524288,"ratio":0.500,"format":"gz","seconds":0.120}]}
```

## Error Handling

squeeze never throws to the user. All errors produce a human-readable message to stderr (or JSON when `--json` is set) and an appropriate exit code.

| Situation | Exit code | Exit reason |
|-----------|-----------|-------------|
| Success | 0 | `success` |
| Input file not found | 1 | `file_not_found` |
| Output file exists (no `--force`) | 1 | `output_exists` |
| Corrupt/unrecognised input | 1 | `corrupt_input` |
| I/O error | 1 | `io_error` |
| Bad arguments | 2 | `usage_error` |
| `-o` with multiple files | 2 | `usage_error` |
| Level out of range | 2 | `usage_error` |

Exit codes match gzip convention (1 = operational error, 2 = usage error).

## Dependencies

- `System.IO.Compression` — built-in: `GZipStream`, `BrotliStream`, `DeflateStream`
- `ZstdSharp.Port` — NuGet package, pure managed C#, AOT-compatible. Provides zstd compression/decompression streams.

## Project Structure

```
src/
  Winix.Squeeze/
    Winix.Squeeze.csproj
    Compressor.cs             ← core compress/decompress stream logic
    FormatDetector.cs         ← magic bytes + extension detection
    Formatting.cs             ← stats output, JSON formatting
    ConsoleEnv.cs             ← inline copy from timeit (pre-ShellKit)
    AnsiColor.cs              ← inline copy from timeit
  squeeze/
    squeeze.csproj
    Program.cs                ← arg parsing, orchestration, exit codes
tests/
  Winix.Squeeze.Tests/
    Winix.Squeeze.Tests.csproj
```

## Testing Strategy

### Unit tests

- **Round-trip:** compress then decompress, verify content matches — for all three formats, at multiple compression levels
- **Format detection:** magic bytes for gzip and zstd, extension fallback for brotli, raw deflate fallback, random bytes rejection
- **Output naming:** extension append (compress), extension strip (decompress), unknown extension error
- **Stats formatting:** ratio calculation, size display, JSON output with standard convention fields
- **Error JSON:** all error types produce valid JSON with correct exit codes and reasons

### Integration tests

- **File mode:** compress a temp file, verify output exists with correct extension, decompress, compare content
- **Pipe simulation:** compress from `MemoryStream`, decompress to `MemoryStream`, verify round-trip
- **Multiple files:** verify each gets its own output
- **`--remove`:** verify input deleted after success, preserved after failure
- **Overwrite protection:** verify error when output exists, success with `--force`
- **Partial cleanup:** verify partial output deleted on corrupt input

### Not testing

- Actual stdin/stdout piping — console app concern, unreliable in test harness
- Compression quality/ratio — that's the underlying library's responsibility

## Explicitly Not In v1

- **No archive/tar integration** — squeeze handles single-file compression, not multi-file archiving
- **No recursive directory compression** — use with `find`/`xargs` or shell globs
- **No encryption** — out of scope for a compression tool
- **No `--rsyncable`** — gzip-specific optimisation, niche use
- **No zlib as output format** — zlib is an internal codec, not a standalone file format
- **No progress bar for large files** — stats after completion are sufficient for v1
