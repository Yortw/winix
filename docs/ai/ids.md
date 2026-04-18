# ids — AI Agent Guide

## What This Tool Does

`ids` generates identifiers — UUID v4, UUID v7 (default), ULID, and NanoID — as a single cross-platform native binary with no runtime dependency.

## When to Use This

- Generating a UUID for a database record, config file, or API payload: `ids`
- Generating sortable/time-ordered IDs for database primary keys: `ids --type uuid7` or `ids --type ulid`
- Generating short URL-safe slugs or tokens: `ids --type nanoid --length 12`
- Batch ID generation: `ids --count 100 > ids.txt`
- Piping IDs into other commands: `ids | clip`, `ids --type nanoid --count 5 | xargs -I{} curl http://host/{}`
- Any context where `uuidgen` would be used but you need UUID v7, ULID, or NanoID — types that `uuidgen` cannot produce

## When NOT to Use This

- When you need a human-memorable slug (e.g. `happy-fox-42`) — `ids` produces opaque identifiers, not word-based slugs
- When you need UUID v1, v3, v5, or v6 — not supported in v1; use `uuidgen` for v1 or a language library for v3/v5
- When you need a custom NanoID alphabet specified as a literal string — named presets (`url-safe`, `alphanum`, `hex`, `lower`, `upper`) are the only options in v1

## Basic Invocation

```bash
# One UUID v7 (default)
ids

# UUID v4
ids --type uuid4

# ULID
ids --type ulid

# NanoID, default 21 chars
ids --type nanoid

# NanoID, 12-char hex token
ids --type nanoid --length 12 --alphabet hex

# Five UUID v7s
ids --count 5

# Windows-registry-style GUID
ids --type uuid7 --format braces --uppercase

# URN-prefixed UUID
ids --type uuid7 --format urn
```

## JSON Output

Pass `--json` for a machine-parseable single JSON array:

```bash
ids --count 3 --json
```

Output shape:
```json
[
  {"id": "018f3c2a-1b4d-7e8f-a1b2-c3d4e5f60718", "type": "uuid7"},
  {"id": "018f3c2a-1b4d-7e8f-a1b2-c3d4e5f60719", "type": "uuid7"},
  {"id": "018f3c2a-1b4d-7e8f-a1b2-c3d4e5f60720", "type": "uuid7"}
]
```

NanoID elements include additional fields:
```json
[
  {"id": "V1StGXR8_Z5jdHi6B-myT", "type": "nanoid", "length": 21, "alphabet": "url-safe"}
]
```

For `--count 1`, the output is still a one-element array (shape is consistent regardless of count).

## Platform Notes

`ids` is fully cross-platform — Windows, Linux, macOS. There is no platform-specific behaviour. The same flags, same output format, same exit codes everywhere. No external helpers required.

## Composability

```bash
# Copy a generated ID to the clipboard
ids | clip

# Generate IDs and make HTTP requests
ids --type nanoid --count 5 | xargs -I{} curl -X POST http://host/resource/{}

# Verify 1000 ULIDs are in monotonic order
ids --type ulid --count 1000 | sort -c

# Generate IDs as JSON for further processing
ids --count 10 --json | jq '.[].id'

# Write 1000 IDs to a file
ids --count 1000 > ids.txt
```

## Flag/Type Compatibility

Some flags only apply to specific ID types; `ids` exits 125 with a clear message on mismatch:

- `--length` and `--alphabet` are NanoID-only
- `--format` applies to UUID v4/v7 only
- `--uppercase` applies to UUID v4/v7 only (ULID is already uppercase; for NanoID use `--alphabet upper`)

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success. |
| 125 | Usage error — invalid flags, unknown values, flag/type mismatch, count/length ≤ 0. |

## Metadata

Run `ids --describe` for full structured metadata (flags, types, examples, exit codes).
Run `ids --help` for human-readable help.
