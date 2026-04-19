# url — AI Agent Guide

## What This Tool Does

`url` is a subcommand CLI for URL manipulation: encode / decode, parse a URL into structured fields, build a URL from parts, resolve a relative URL against a base (RFC 3986 §5), and edit query strings while preserving order and duplicate keys. Single cross-platform AOT native binary.

## When to Use This

- Building URLs from user-supplied values in scripts where per-value encoding matters: `url build --host api.example.com --query q="$user_input"`
- Extracting a field from a URL without jq: `HOST=$(url parse "$u" --field host)`
- Resolving a relative URL against a base correctly (dot-segments, query-only refs, etc.): `url join "$base" "$relative"`
- Editing a query string (set/add/remove keys): `url query set "$url" page 2`
- Anywhere you'd reach for Python's `urllib.parse` and want a consistent cross-shell surface plus AI-discoverable `--describe` output

## When NOT to Use This

- For URL validation (is this a URL?) — `url parse` will tell you, but the use case is thin; use `[[ "$s" =~ ^https?:// ]]` or similar for simple checks
- For HTML entity encode/decode — different domain, not what `url` handles
- For batch processing many URLs in one call — v1 is one URL per invocation; use shell loops

## Basic Invocation

```bash
# Encode / decode
url encode "hello world"                  # hello%20world
url encode "hello world" --form           # hello+world (space → +)
url decode "hello%20world"                # hello world
url decode "hello+world" --form           # hello world (+ as space)

# Parse
url parse "https://api.example.com:8443/v1/users?q=hello&limit=10#top"
url parse "https://x.io/" --field host    # x.io
url parse "https://x.io/?a=1&b=2" --json  # structured JSON

# Build
url build --host api.example.com --path /v1/users \
          --query q="hello world" --query limit=10

# Join (RFC 3986 §5)
url join "https://example.com/blog/" "./2026/post-1"
url join "https://example.com/api/v1/users" "/login"

# Query editing
url query get    "https://x.io/?a=1&b=2" a
url query set    "https://x.io/?a=1" b 2
url query delete "https://x.io/?a=1&b=2" a
```

## JSON Output

`url parse --json` emits a structured shape:

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

- Absent fields are `null` (distinguishes "no port" from "port 0").
- `query` is always an array — preserves order AND duplicate keys. Important because `?tag=a&tag=b` is semantically different from `?tag=b` in many APIs.
- No array de-duplication; no key sorting.

## Encoding Modes

| Mode | Space | Preserves `/` | Use case |
|---|---|---|---|
| `component` (default) | `%20` | no | Safe for any URL component. Matches JavaScript `encodeURIComponent`. |
| `path` | `%20` | yes | Building URL path segments. |
| `query` | `%20` | no | Query-string values. |
| `form` | `+` | no | `application/x-www-form-urlencoded`. |

`--form` is a shorthand for `--mode form` and applies to both `encode` and `decode`.

## Normalisation

- `parse` reflects input except for host case (.NET's `Uri` lowercases hosts per RFC 3986 §3.2.2) and decoded unreserved escapes.
- `build`, `join`, `query set`, `query delete` normalise output (lowercase scheme, strip default ports).
- `--raw` disables normalisation where it would apply, but the result is still syntax-validated.
- UserInfo is preserved across `query set` / `query delete` operations (basic-auth URLs survive query edits).
- Unicode hostnames pass through without auto-Punycoding. Applications needing Punycode can post-process externally.

## Platform Notes

Fully cross-platform. No platform-specific behaviour.

**Git Bash caveat:** MSYS path-translation converts `/foo` arguments to `C:\Program Files\Git\foo` by default. Set `MSYS_NO_PATHCONV=1` or use double-slash (`//foo`) to pass a literal `/foo` argument on Windows Git Bash. This affects `--path /v1` on `url build` and `/login` relative args on `url join`. Not a url issue; a Git Bash quirk.

## Composability

```bash
# Build a URL from a generated ID
ids --type uuid7 | xargs -I{} url build --host api.example.com --path /v1/resources/{}

# Retried request with a constructed URL
retry -- curl "$(url build --host api.example.com --path /health)"

# Extract host, feed to digest
url parse "$url" --field host | digest --sha256 -

# Build + clip (copy constructed URL to clipboard)
url build --host api.example.com --path /v1 | clip

# Pipeline: parse, edit, rebuild
url query set "$(url build --host x.io --path /api)" page 2

# Follow links from an HTML scraper
while read link; do
  url join "$base" "$link"
done
```

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success. |
| 125 | Usage error — bad flags, unknown subcommand, missing required value, base URL not absolute. |
| 126 | Runtime error — invalid URL syntax, `query get` key not found. |

## Metadata

Run `url --describe` for full structured metadata (subcommands, flags, examples, exit codes, JSON field shape).
Run `url --help` for human-readable help.
