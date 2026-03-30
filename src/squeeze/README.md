# squeeze

Compress and decompress files using gzip, brotli, or zstd.

Supports pipe mode (stdin/stdout), multi-file batch processing, and gzip-compatible flags for muscle memory.

**Multi-format `gzip` replacement** (and works on Linux/macOS too).

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/squeeze
```

### Winget (Windows, stable releases)

```bash
winget install Winix.Squeeze
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.Squeeze
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).

## Usage

```
squeeze [options] [file...]
```

### Examples

```bash
# Compress a file (gzip, keeps original)
squeeze data.csv

# Decompress (auto-detects format)
squeeze -d data.csv.gz

# Use brotli at maximum compression
squeeze --brotli --level 11 largefile.bin

# Use zstd (fast default)
squeeze --zstd data.csv

# Pipe mode — compress stdin to stdout
cat data.csv | squeeze > data.csv.gz

# Pipe mode — decompress
cat data.csv.gz | squeeze -d > data.csv

# Stdout mode — decompress to stdout
squeeze -c -d archive.gz | head -20

# Explicit output file
squeeze -o compressed.gz data.csv

# gzip-compatible shortcuts
squeeze -9 data.csv          # max compression
squeeze -1 data.csv          # fastest compression
squeeze -d -c archive.gz     # decompress to stdout

# Batch compress
squeeze *.log

# JSON output for scripts
squeeze --json data.csv
```

### Output Formats

**Human** (terminal, stderr):
```
data.csv → data.csv.gz  1,234,567 → 456,789 (63.0% saved)  gzip/6  0.12s
```

**JSON** (`--json`, stderr):
```json
{"tool":"squeeze","version":"0.1.0","exit_code":0,"exit_reason":"success","files":[{"input":"data.csv","output":"data.csv.gz","input_bytes":1234567,"output_bytes":456789,"format":"gzip","level":6,"seconds":0.12}]}
```

## Options

| Option | Description |
|--------|-------------|
| `-d`, `--decompress` | Decompress (auto-detects format from magic bytes) |
| `-b`, `--brotli` | Use brotli format |
| `-z`, `--zstd` | Use zstd format |
| `--level N` | Compression level (see table below) |
| `-1`..**`-9`** | Shortcut for `--level 1`..`--level 9` |
| `-c`, `--stdout` | Write to stdout instead of creating output file |
| `-o`, `--output FILE` | Explicit output file (single input only; `-` for stdout) |
| `-f`, `--force` | Overwrite existing output files |
| `--remove` | Delete input file after successful operation |
| `-k`, `--keep` | Keep original file (default, accepted for gzip compat) |
| `-v`, `--verbose` | Show stats even when piped |
| `-q`, `--quiet` | Suppress stats even on terminal |
| `--json` | JSON output to stderr |
| `--no-color` | Disable colored output |
| `--color` | Force colored output |
| `--version` | Show version |
| `-h`, `--help` | Show help |

### Compression Levels

| Format | Range | Default | Notes |
|--------|-------|---------|-------|
| gzip | 1-9 | 6 | Standard deflate |
| brotli | 0-11 | 6 | Higher levels much slower but smaller |
| zstd | 1-22 | 3 | Fast default, excellent ratio at higher levels |

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Compression/decompression error |
| 2 | Usage error (bad arguments) |

## Colour

- Automatic: colour when outputting to a terminal, plain when piped
- `--color` forces colour on (overrides `NO_COLOR`)
- `--no-color` forces colour off
- Respects the `NO_COLOR` environment variable ([no-color.org](https://no-color.org))

## Part of Winix

`squeeze` is part of the [Winix](../../README.md) CLI toolkit.
