# qr

Cross-platform QR code generator with helpers for Wi-Fi, SMS, mailto, geo, and tel payloads. Single AOT native binary. Fully offline — no online-generator privacy leak.

## Why

Online QR generators are a privacy hole: pasting a Wi-Fi password, a crypto address, or an internal URL into a web form leaks it. `qr` closes that gap. It's also cross-platform, so the same command works on Windows, macOS, and Linux.

## Install

### Scoop (Windows)

```
scoop bucket add winix https://github.com/Yortw/winix
scoop install qr
```

### .NET global tool (any platform with .NET 10+)

```
dotnet tool install --global Winix.Qr
```

### Native binary (GitHub Releases)

Download the platform-appropriate archive from [releases](https://github.com/Yortw/winix/releases) and place `qr` on your PATH.

## Usage

```bash
qr "https://example.com"                       # unicode QR to stdout (TTY)
qr "https://example.com" > code.svg            # SVG auto-selected when piped
qr "https://example.com" --format png -o code.png

qr wifi --ssid HomeNet --password s3cr3t --security wpa2
qr sms --number +15551234 --message "Hello"
qr mailto --to a@b.com --subject "Bug report"
qr geo --lat -41.2924 --lon 174.7787 --query "Wellington NZ"
qr tel --number +6441234567

echo "https://example.com" | qr                # stdin
```

## Options

| Flag | Default | Description |
|---|---|---|
| `--format {unicode,ascii,svg,png}` | `auto` | Output format. `auto` = unicode on TTY, svg when piped. |
| `--size N` / `-s N` | `10` | Pixels per module (PNG/SVG only). |
| `--error-correction {l,m,q,h}` / `-e X` | `m` | ECC level. `m`=15% recovery (default), `h`=30%. |
| `--no-margin` | off | Strip the 4-module quiet zone. May reduce scannability. |
| `--output PATH` / `-o PATH` | stdout | Write to file instead of stdout. Refuses to overwrite existing files unless `--force` is passed. |
| `--force-binary` | off | Allow PNG output to a TTY (otherwise refused). |
| `--force` | off | Overwrite an existing `--output` file (refused by default to avoid losing user data). Has no effect — and is rejected as a usage error — if `--output` is not also supplied. |
| `--describe` | off | Emit tool metadata as JSON. |
| `--help`, `--version` | — | Standard introspection. |
| `--color`, `--no-color` | — | Respect `NO_COLOR`. |

### Subcommand flags

**`qr wifi`** — `--ssid <s>`, `--password <p>`, `--security {wpa2,wpa,wep,nopass}` (default `wpa2`), `--hidden`.

**`qr sms`** — `--number <n>` (required), `--message <m>`. The number is sanitised by the same rules as `qr tel`.

**`qr mailto`** — `--to <a>` (required), `--subject <s>`, `--body <b>`, `--cc <a>`, `--bcc <a>`.

**`qr geo`** — `--lat <n>`, `--lon <n>` (both required; lat ∈ [-90, 90], lon ∈ [-180, 180]), `--query <q>`.

**`qr tel`** — `--number <n>` (required). Phone numbers are sanitised: ASCII whitespace is stripped, and only digits, an optional leading `+`, separators `()-./*#`, and RFC 3966 `;param=value` extensions are accepted. Letters and shell metacharacters are rejected.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Success. |
| 125 | Usage error: bad flags, missing required field, empty payload, PNG to TTY without `--force-binary`, `--format` contradicts `--output` extension, refusing to overwrite an existing `--output` file without `--force`, `--force` supplied without `--output`, `--output` path empty or whitespace, helper field value violates its grammar (e.g. tel/sms with letters, geo coordinates out of range). |
| 126 | Runtime error: payload exceeds QR capacity (try `--error-correction l` or shorten payload), invalid UTF-8 on stdin, output file write failed (parent directory missing, permission denied, path too long, etc.). |

`--format svg --output code.png` (and similar contradictions) are rejected at parse time so downstream tools that route on extension don't get content with a misleading suffix.

## Name collisions

`qr` is also the script installed by the Python `qrcode` package. If both are installed, PATH order decides which runs.

## Colour

Unicode output uses the terminal default foreground/background. `NO_COLOR` is respected by honoring terminal settings (no ANSI sequences are emitted).
