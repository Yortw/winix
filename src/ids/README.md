# ids

Cross-platform identifier generator — UUID v4, UUID v7 (default), ULID, and NanoID. Single native binary, no runtime, consistent flag surface across all ID types.

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/ids
```

### Winget (Windows, stable releases)

```bash
winget install Winix.Ids
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.Ids
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).

## Usage

```
ids [options]
```

### Examples

```bash
# One UUID v7 (default)
ids
# 018f3c2a-1b4d-7e8f-a1b2-c3d4e5f60718

# Random UUID v4
ids --type uuid4
# 550e8400-e29b-41d4-a716-446655440000

# One ULID — 26 Crockford base-32 chars, uppercase, time-ordered
ids --type ulid
# 01HZ3QKPZ7X4YBRV2WDSM6NJGF

# NanoID — default 21-char URL-safe string
ids --type nanoid
# V1StGXR8_Z5jdHi6B-myT

# Short hex token (12-char NanoID using hex alphabet)
ids --type nanoid --length 12 --alphabet hex
# 3f8a2c1d9b04

# Five UUID v7s, one per line
ids --count 5
# 018f3c2a-1b4d-7e8f-a1b2-c3d4e5f60718
# 018f3c2a-1b4d-7e8f-a1b2-c3d4e5f60719
# ...

# Windows-registry-style GUID (braces, uppercase)
ids --type uuid7 --format braces --uppercase
# {018F3C2A-1B4D-7E8F-A1B2-C3D4E5F60718}

# URN-prefixed UUID
ids --type uuid7 --format urn
# urn:uuid:018f3c2a-1b4d-7e8f-a1b2-c3d4e5f60718

# JSON array of three IDs
ids --count 3 --json
# [{"id":"018f3c2a-1b4d-7e8f-a1b2-c3d4e5f60718","type":"uuid7"},...]

# Pipe an ID to the clipboard
ids | clip

# Generate 1000 ULIDs and verify they are in monotonic order
ids --type ulid --count 1000 | sort -c
```

## Options

| Flag | Short | Default | Description |
|---|---|---|---|
| `--type T` | `-t T` | `uuid7` | ID type to generate: `uuid4`, `uuid7`, `ulid`, `nanoid`. |
| `--count N` | `-n N` | `1` | Number of IDs to generate (must be ≥ 1). |
| `--length N` | `-l N` | `21` | Output length. NanoID only — exit 125 if used with other types. |
| `--alphabet A` | | `url-safe` | NanoID alphabet: `url-safe`, `alphanum`, `hex`, `lower`, `upper`. Exit 125 with other types. |
| `--format F` | | `default` | UUID output format: `default` (hyphenated), `hex` (32 hex chars), `braces` (`{…}`), `urn` (`urn:uuid:…`). UUID v4/v7 only — exit 125 with ULID or NanoID. |
| `--uppercase` | `-u` | off | Uppercase UUID output. UUID v4/v7 only — exit 125 with ULID (already uppercase) or NanoID (use `--alphabet upper`). |
| `--json` | | off | Emit a single JSON array to stdout. Each element has `"id"` and `"type"` fields; NanoID elements also include `"length"` and `"alphabet"`. |
| `--describe` | | | Emit structured JSON metadata for AI discoverability. |
| `--help` | `-h` | | Show help and exit. |
| `--version` | `-v` | | Show version and exit. |
| `--color WHEN` | | `auto` | `auto`, `always`, or `never`. Respects `NO_COLOR`. |
| `--no-color` | | | Equivalent to `--color never`. |

## ID Types

| Type | Output | Description |
|---|---|---|
| `uuid7` | `018f3c2a-1b4d-7e8f-a1b2-c3d4e5f60718` | **Default.** Time-ordered UUID v7. Sortable, database-friendly, 128-bit. Prefer over v4 when you need lexicographic sortability. |
| `uuid4` | `550e8400-e29b-41d4-a716-446655440000` | Random UUID v4. No time component. Use when sortability is irrelevant and you want the widely familiar format. |
| `ulid` | `01HZ3QKPZ7X4YBRV2WDSM6NJGF` | Universally Unique Lexicographically Sortable Identifier. 26 Crockford base-32 uppercase chars, 48-bit timestamp + 80 bits of randomness. Compact and URL-safe. |
| `nanoid` | `V1StGXR8_Z5jdHi6B-myT` | Configurable-length URL-safe string. Default 21 chars with `url-safe` alphabet. Use `--length` and `--alphabet` to customise. |

### NanoID Alphabets

| Alphabet | Chars | Example output |
|---|---|---|
| `url-safe` (default) | A–Za–z0–9`_-` (64) | `V1StGXR8_Z5jdHi6B-myT` |
| `alphanum` | A–Za–z0–9 (62) | `V1StGXR8Z5jdHi6BmyT34` |
| `hex` | 0–9a–f (16) | `3f8a2c1d9b04e7` |
| `lower` | a–z0–9 (36) | `v1stgxr8z5jdhi6` |
| `upper` | A–Z0–9 (36) | `V1STGXR8Z5JDHI6` |

## Monotonicity Guarantee

For time-ordered ID types (UUID v7 and ULID), `ids` guarantees monotonicity within each millisecond: if two IDs are generated in the same millisecond, the second one sorts strictly after the first. This means `ids --type ulid --count 1000 | sort -c` succeeds — the output is already sorted. UUID v7 monotonicity is enforced with an application-level guard because .NET 10's `Guid.CreateVersion7()` does not guarantee intra-millisecond ordering on its own.

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success. |
| 125 | Usage error — invalid flags, unknown type/format/alphabet, flag/type mismatch, or count/length ≤ 0. |

## Colour

`ids` output is plain (no coloured output). The `--color` and `--no-color` flags are accepted for suite consistency. `NO_COLOR` is respected.

## Naming Note

The tool is named `ids` (plural) rather than `id` to avoid shadowing the POSIX `id` coreutil, which prints the current user's UID and GID on Linux and macOS. Shadowing `id` would break scripts that rely on it.

## Related Tools

- [`clip`](../clip/README.md) — pipe generated IDs to the clipboard: `ids | clip`
- Compose freely with standard Unix tools: `ids --count 5 | xargs -I{} curl http://host/resource/{}`

## See Also

- `man ids` (after `winix install man`)
- `ids --describe` for JSON metadata
