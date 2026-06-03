# url

Cross-platform URL encode/decode/parse/build/join/query-edit CLI. Single native binary, no runtime, consistent surface across Windows, macOS, and Linux. Fills the cross-shell inconsistency gap around URL assembly — especially on cmd.exe where subprocess substitution is clunky and per-value encoding is impractical.

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/url
```

### Winget (Windows, stable releases)

```bash
winget install Winix.Url
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.Url
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).

## Usage

```
url encode <string>
url decode <string>
url parse  <url>
url build  --scheme S --host H [options]
url join   <base> <relative>
url query  get    <url> <key>
url query  set    <url> <key> <value>
url query  delete <url> <key>
```

### Examples

```bash
# Encode / decode
url encode "hello world"                 # hello%20world
url encode "hello world" --form          # hello+world (space → +)
url decode "hello%20world"               # hello world
url decode "hello+world" --form          # hello world

# Parse a URL into key=value lines
url parse "https://api.example.com:8443/v1/users?q=hello&limit=10#top"

# Extract a single field (no jq needed)
url parse "https://api.example.com/v1" --field host
# api.example.com

# JSON output — preserves query order and duplicate keys
url parse "https://x.io/?a=1&b=2&a=3" --json

# Assemble a URL from parts — per-value encoded
url build --host api.example.com --path /v1/users \
          --query q="hello world" --query limit=10
# https://api.example.com/v1/users?q=hello+world&limit=10

# RFC 3986 §5 relative-URL resolution
url join "https://example.com/blog/" "./2026/post-1"
# https://example.com/blog/2026/post-1

url join "https://example.com/api/v1/users" "/login"
# https://example.com/login

# Edit query parameters in-place
url query get    "https://x.io/?a=1&b=2" a        # 1
url query set    "https://x.io/?a=1" b 2          # https://x.io/?a=1&b=2
url query delete "https://x.io/?a=1&b=2" a        # https://x.io/?b=2

# Get every value when a key has duplicates (default is first-only with stderr warning)
url query get "https://x.io/?a=1&a=2&a=3" a --all
# 1
# 2
# 3

# Strict decode rejects malformed percent-escapes (default is lenient)
url decode "a%" --strict        # exit 126, "malformed percent-escape at position 1"

# Compose with other Winix tools
ids --type uuid7 | xargs -I{} url build --host api.example.com --path /v1/resources/{}
retry -- curl "$(url build --host api.example.com --path /v1/health)"
```

## Subcommands

| Subcommand | Positionals | Purpose |
|---|---|---|
| `encode` | STRING | Percent-encode a string per the `--mode` variant. |
| `decode` | STRING | Percent-decode a string; `--form` or `--mode form` flips `+` to space. |
| `parse` | URL | Deconstruct into fields. Plain `key=value` lines, `--json`, or `--field NAME`. |
| `build` | (none; flags) | Assemble a URL from `--scheme --host --port --path --query K=V --fragment`. |
| `join` | BASE RELATIVE | RFC 3986 §5 resolution: resolve RELATIVE against BASE. |
| `query get` | URL KEY | Read the first value for KEY (use `--all` for every value). Stderr warning when duplicates exist. |
| `query set` | URL KEY VALUE | Set the first occurrence; collapse remaining duplicates. Stderr warning if more than one was collapsed. |
| `query delete` | URL KEY | Remove all values for KEY. Stderr warning when more than one was removed. |

## Global Flags

Subcommand-specific flags are gated — passing one on a subcommand it doesn't apply to is rejected with a usage error rather than silently ignored.

| Flag | Default | Applies to | Description |
|---|---|---|---|
| `--mode {component,path,query,form}` | `component` | encode / decode | Encoding variant. |
| `--form` | off | encode / decode | Shorthand for `--mode form` (space ↔ `+`). |
| `--strict` | off | decode | Reject malformed percent-escapes (lone `%`, `%X`, `%XY` where X/Y not hex). Lenient default passes them through verbatim. |
| `--raw` | off | build / join / query set+delete | Disable URL normalisation. |
| `--field NAME` | none | parse | Emit a single field; conflicts with `--json`. |
| `--all` | off | query get | Emit every value (one per line) for keys with duplicates. Without `--all`, prints the first value and warns on stderr that more exist. |
| `--json` | off | parse | Structured JSON output. |
| `--describe` | | | Emit structured JSON metadata for AI discoverability. |
| `--help` `-h` | | | Show help and exit. |
| `--version` `-v` | | | Show version and exit. |
| `--color[=auto\|always\|never]` | `auto` | | Coloured output: auto (default when omitted), always, or never. |
| `--no-color` | | | Disable coloured output. Respects `NO_COLOR`. |

## Build Flags

| Flag | Required | Description |
|---|---|---|
| `--scheme S` | no (default `https`) | URL scheme. |
| `--host H` | yes | Hostname. |
| `--port N` | no | Port (1-65535). Omitted if default for scheme unless `--raw`. |
| `--path P` | no | URL path. Leading `/` added if missing; each segment path-mode-encoded. |
| `--query K=V` | no (repeatable) | Query pair. Both K and V form-encoded. |
| `--fragment F` | no | Fragment without `#`. Component-encoded. |

## Encoding Modes

| Mode | Alphabet | Space | Use case |
|---|---|---|---|
| `component` (default) | RFC 3986 unreserved | `%20` | Safe for any URL component. Matches JavaScript `encodeURIComponent`. |
| `path` | Same + `/` preserved | `%20` | Building URL path segments. |
| `query` | Same as component | `%20` | Query-string values where space should be `%20`. |
| `form` | Same as component | `+` | `application/x-www-form-urlencoded`. |

## Security & Validation

`url` runs untrusted-input-shaped operations (host assembly, relative URL resolution, query manipulation) so a few rules are enforced on inputs to prevent silent misinterpretation:

### `--host` rejects URL-component characters

`url build --host VALUE` rejects values that contain `/`, `?`, `#`, `@`, or any whitespace / control character. Without this, `--host "evil.com/@trusted.com"` would assemble a URL whose host portion silently differs from what the user typed (`@` re-parsed as userinfo, `/` splits at the path boundary). Control characters report position + hex code in the error so invisible characters (vertical tab, null byte) are diagnosable.

`Uri.CheckHostName` is also consulted for general well-formedness — empty DNS labels (`foo..bar`) and similar are rejected.

### `url join` scheme allowlist

`url join` accepts only hierarchical schemes that have authority semantics: `http`, `https`, `ws`, `wss`, `ftp`, `ftps`, `ssh`, `file`. Opaque schemes (`javascript:`, `mailto:`, `data:`, `urn:`, etc.) are rejected with a clear error naming the allowed set.

This prevents scheme-smuggling — without the allowlist, `url join "javascript:foo" "bar"` would emit `javascript:bar` which is a foothold in pipelines that pass the joined result to a browser/renderer.

### Query keys are decoded before lookup

`query get` / `set` / `delete` percent-decode the user-supplied key before matching. Both `url query get URL "a=b"` and `url query get URL "a%3Db"` look up the same key — so round-tripping a key through `--field query` (which preserves wire form) and back to `query get` works for keys containing encoded characters.

### Decoder strict mode

`url decode --strict` rejects malformed percent-escapes (`a%`, `a%X`, `a%ZZ`) with a position-specific error. Default behaviour is lenient (matches `Uri.UnescapeDataString`) — input passes through verbatim — so a tool whose job is round-tripping can't distinguish "input was plain" from "input was corrupted" without the opt-in.

## Normalisation

| Subcommand | Normalised? |
|---|---|
| `encode` / `decode` | N/A — string-level. |
| `parse` | .NET's `Uri` always lowercases the host (`HTTPS://Example.COM` → `example.com` in the `host` field) and decodes unreserved escapes. Other fields reflect input. |
| `build` | Lowercase scheme, strip default port unless `--raw`. `--raw` still validates the result syntactically. |
| `join` | Yes, unless `--raw`. |
| `query set` / `delete` | Yes (rebuilt via `build`), unless `--raw`. UserInfo (e.g. `user:pw@`) is preserved. |

**IDN:** non-ASCII hostnames pass through as-is (Unicode preserved). Applications that need Punycode can re-encode externally (e.g. via `System.Uri.IdnHost` if scripting from .NET); `url` does not force the conversion. Future v2 may add `--idn` opt-in.

## JSON Output Shape (`url parse --json`)

```json
{
  "scheme": "https",
  "userinfo": null,
  "host": "api.example.com",
  "port": 8443,
  "path": "/v1/users",
  "query": [
    {"key": "q", "value": "hello world"},
    {"key": "limit", "value": "10"}
  ],
  "fragment": "top"
}
```

- `null` for absent fields — distinguishes "no port" from "port 0".
- `query` is always an array — even with zero or one key. Preserves order and duplicate keys.

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success. |
| 125 | Usage error — bad flags, unknown subcommand, missing required value, base URL not absolute. |
| 126 | Runtime error — invalid URL syntax, `query get` key not found. |

## `--field` output conventions

| Field | Output form |
|---|---|
| `scheme` | lowercased (BCL behaviour) |
| `userinfo` | raw (as-in-URL, still percent-encoded) |
| `host` | lowercased Unicode (BCL behaviour) |
| `port` | integer as decimal string |
| `path` | percent-encoded (as-in-URL, round-trips with `url build --path`) |
| `query` | form-encoded raw query string (what `url parse` would emit) |
| `fragment` | percent-**decoded** (human-readable) |

If you need the URL-encoded form of a field that's decoded by default, use `--json` and `jq` rather than `--field`.

## Windows Git Bash note

Git Bash on Windows auto-translates any argument starting with `/` into a Windows path (e.g. `/v1` → `C:\Program Files\Git\v1`). This affects `--path /v1` on `url build` and `/login` relative args on `url join`. Workaround:

```bash
MSYS_NO_PATHCONV=1 url build --host x.io --path /v1
# or use a double-slash prefix:
url build --host x.io --path //v1     # preserved by MSYS as /v1
```

Not a url issue — MSYS quirk inherited by Git Bash.

## Differences from `python -c "import urllib.parse"`

- Single native binary, ~290ms cold start vs Python's ~100ms + interpreter cold-load.
- `url join` as a top-level operation (Python has `urljoin`, but most scripters don't know it).
- `--describe` / `--json` for AI-agent discovery.
- Consistent surface across all shells (bash, zsh, fish, PowerShell, cmd).
- Co-ships with the Winix suite — no separate install.

## Related Tools

- [`ids`](../ids/README.md) — generate IDs to use as path components: `ids | xargs -I{} url build --host api.example.com --path /v1/resources/{}`
- [`retry`](../retry/README.md) — retry failed requests: `retry -- curl "$(url build ...)"`
- [`digest`](../digest/README.md) — hash URL responses
- [`clip`](../clip/README.md) — copy a built URL to the clipboard: `url build ... | clip`

## See Also

- `man url` (after `winix install man`)
- `url --describe` for JSON metadata
- [RFC 3986](https://datatracker.ietf.org/doc/html/rfc3986) — URI Generic Syntax
- [RFC 3986 §5](https://datatracker.ietf.org/doc/html/rfc3986#section-5) — Reference Resolution (used by `url join`)
