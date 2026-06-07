# mksecret

Cross-platform secret generator — random passwords, EFF diceware passphrases, and encoded high-entropy keys. Single native binary, no runtime, always CSPRNG.

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/mksecret
```

### Winget (Windows, stable releases)

```bash
winget install Winix.MkSecret
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.MkSecret
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).

## Usage

```
mksecret [password] [options]
mksecret phrase [options]
mksecret key [options]
```

Running `mksecret` without a subcommand defaults to `password` mode. Run `mksecret SUBCOMMAND --help` for mode-specific flags.

An `≈ N bits` entropy estimate is written to stderr after generation unless `--quiet` or `--json` is given. It is informational only and does not affect the generated value.

### password — random-character password

Generates a password by selecting characters uniformly at random from the chosen character set using the OS CSPRNG.

```bash
# 20-char alphanumeric password (default)
mksecret
# a3Rk8Qz2nTpL7mWvYf4J

# 32 chars including symbols
mksecret --length 32 --charset full
# t*7#Zq!nK3@wL8$rP2&mB6^Xv9%Ys0(

# Avoid visually-ambiguous characters (l/1/I/O/0/o)
mksecret --charset safe
# a3Rk8QzAnTpK7mWvYf4J

# Five passwords, one per line
mksecret --count 5

# JSON envelope
mksecret --count 3 --json
# {"mode":"password","bits":119.1,"values":["a3Rk8Qz2nTpLvW7yX1mB","...",...]}
```

#### password options

| Flag | Short | Default | Description |
|---|---|---|---|
| `--length N` | `-l N` | `20` | Password length in characters (max 4096). |
| `--charset NAME` | `-c NAME` | `alphanumeric` | Character set. See table below. |
| `--count N` | `-n N` | `1` | Number of passwords to generate. |
| `--json` | | off | Emit a JSON envelope to stdout. |
| `--quiet` | | off | Suppress the stderr entropy note. |

#### Character sets

| Name | Chars | Size | Notes |
|---|---|---|---|
| `alphanumeric` (default) | A–Za–z0–9 | 62 | Safe for most systems with no quoting concerns. |
| `full` | All printable ASCII 33–126 | 94 | Includes symbols — maximum density, but may need shell quoting. |
| `alpha` | A–Za–z | 52 | Letters only. |
| `digits` | 0–9 | 10 | Digits only. |
| `safe` | Alphanumeric minus l, 1, I, O, 0, o | 56 | Avoids visually-ambiguous characters; useful for spoken/typed sharing. |

### phrase — EFF diceware passphrase

Selects words uniformly at random from the EFF long wordlist (7776 words, ~12.9 bits/word). Six words gives approximately 77 bits of entropy.

```bash
# Six-word passphrase, hyphen-separated (default)
mksecret phrase
# cosmic-table-river-beyond-flame-dusk

# Eight words, space-separated
mksecret phrase --words 8 --sep ' '
# cosmic table river beyond flame dusk amber frost

# Title-cased with a trailing digit
mksecret phrase --capitalize --number
# Cosmic-Table-River-Beyond-Flame-Dusk4

# JSON envelope
mksecret phrase --json
# {"mode":"phrase","bits":77.5,"values":["cosmic-table-river-beyond-flame-dusk"]}
```

#### phrase options

| Flag | Short | Default | Description |
|---|---|---|---|
| `--words N` | `-w N` | `6` | Number of words (max 1024). |
| `--sep STR` | `-s STR` | `-` | Separator between words. |
| `--capitalize` | | off | Capitalise the first letter of each word. |
| `--number` | | off | Append a random digit to the passphrase. |
| `--count N` | `-n N` | `1` | Number of passphrases to generate. |
| `--json` | | off | Emit a JSON envelope to stdout. |
| `--quiet` | | off | Suppress the stderr entropy note. |

### key — encoded high-entropy key

Generates N random bytes from the OS CSPRNG and encodes them. Suitable for API keys, OAuth secrets, HMAC signing keys, and session tokens.

```bash
# 32 bytes as unpadded base64url (default — 256-bit key)
mksecret key
# Xk2mR8nQpL7tW3vYf4Jz9aHdCeGbFiKs

# 64 bytes as hex
mksecret key --bytes 64 --encoding hex
# 3f8a2c1d9b04e7...

# Crockford base32 (no ambiguous characters)
mksecret key --encoding base32

# Three keys, one per line
mksecret key --count 3

# JSON envelope
mksecret key --json
# {"mode":"key","bits":256.0,"values":["Xk2mR8nQpL7tW3vYf4Jz9aHdCeGbFiKs"]}
```

#### key options

| Flag | Short | Default | Description |
|---|---|---|---|
| `--bytes N` | `-b N` | `32` | Number of random bytes (max 65536). |
| `--encoding NAME` | `-e NAME` | `base64url` | Encoding: `base64url` (unpadded), `base64`, `hex`, `base32`. |
| `--count N` | `-n N` | `1` | Number of keys to generate. |
| `--json` | | off | Emit a JSON envelope to stdout. |
| `--quiet` | | off | Suppress the stderr entropy note. |

### Common flags (all modes)

| Flag | Description |
|---|---|
| `--describe` | Emit structured JSON metadata for AI discoverability. |
| `--help`, `-h` | Show help and exit. |
| `--version` | Show version and exit. |
| `--color[=auto\|always\|never]` | Coloured output: auto (default when omitted), always, or never. |
| `--no-color` | Disable coloured output. Respects `NO_COLOR`. |

## Copying to the clipboard

Pipe generated secrets directly to `clip` — they never touch a shell history file, a temp file, or the terminal display:

```bash
# Copy a password
mksecret | clip

# Copy a passphrase
mksecret phrase | clip

# Copy a 32-byte key
mksecret key --bytes 32 | clip
```

**Clipboard security caveat.** Clipboard contents may be persisted by clipboard history managers, synced across devices (e.g. Windows clipboard history, iCloud clipboard on macOS), and are readable by any process with clipboard access. `mksecret` does not attempt to clear the clipboard after copying — use `clip --clear` after you have pasted, or disable clipboard history for sensitive secrets.

## Using a generated key with digest

An HMAC or signing key must be **persisted** to stay verifiable — the same key must be present at both sign and verify time. The correct pattern is generate-then-store:

```bash
# Generate a 32-byte HMAC key and save it
mksecret key --bytes 32 > signing.key

# Sign a payload using the stored key
digest --hmac sha256 --key-file signing.key "payload"

# Verify later — same key, same payload
digest --hmac sha256 --key-file signing.key "payload"
```

**Do NOT do this:**

```bash
mksecret key | digest --hmac sha256 --key-stdin "payload"
```

The key is discarded as soon as the pipe closes. You get a valid-looking MAC but it can never be verified — you have neither the key nor a consistent way to reproduce it.

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success. A closed downstream pipe (e.g. `mksecret --count 100000 \| head -1`) also exits 0 — it is not an error. |
| 125 | Usage error — unknown subcommand, invalid flag, bad `--charset`/`--encoding` value, non-positive or oversized count/length/bytes/words, unexpected positional. Stderr carries the message. |
| 126 | Runtime error — OS CSPRNG failure or output write failure (disk full, device error). Stderr carries the message. |

## Colour

`mksecret` output is plain (no coloured output). The `--color` and `--no-color` flags are accepted for suite consistency. `NO_COLOR` is respected.

## Related Tools

- [`clip`](../clip/README.md) — copy generated secrets to the clipboard: `mksecret | clip`
- [`digest`](../digest/README.md) — compute HMAC using a stored key: `digest --hmac sha256 --key-file signing.key "payload"`
- [`ids`](../ids/README.md) — generate identifiers (UUID, ULID, NanoID) rather than secrets

## See Also

- `man mksecret` (after `winix install man`)
- `mksecret --describe` for JSON metadata
