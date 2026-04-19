# url — Design

**Date:** 2026-04-20
**Tool:** `url`
**Release:** v0.4.0
**Status:** Approved

## Goal

`url` is a CLI for URL encode / decode / parse / build / join / query-edit. Single AOT native binary. Fills the cross-shell inconsistency gap around URL assembly and manipulation — especially on cmd.exe where subprocess substitution is clunky and per-value encoding is impractical.

## Positioning

Not a hard gap-fill on the POSIX side — `python -c "import urllib.parse"` works where Python's installed. But:

- **cmd.exe is genuinely bad at this.** No `$(...)` substitution, so calling `url encode "$value"` to get a result into a variable needs the `for /F "tokens=*"` incantation. Most users give up and produce broken URLs.
- **Multi-param query building is painful in every shell.** Loop + per-value subprocess + manual `&` joining. A single `url build` call absorbs the cost into one startup.
- **Relative URL resolution (`url join`) is a real gap.** RFC 3986 §5 resolution handles dot-segments, query-only refs, fragment-only refs, absolute-path relatives, and protocol-relative URLs. You can't get that right with shell string concat. Even Python's one-liner requires knowing `urljoin` exists.
- **Consistent `--describe` / `--json` surface** for AI-agent composition.
- **Co-ships with the Winix suite** — no extra install, ~290ms startup.

**Cross-suite synergy:** `ids --type uuid7 | xargs -I{} url build --scheme https --host api.example.com --path /v1/resources/{}` — compose ID generation and URL assembly. `retry -- curl "$(url build ...)"` for retried requests.

## CLI Interface

**Subcommands:**

```
url encode <string>
url decode <string>
url parse <url>
url build --scheme S --host H [options]
url join <base> <relative>
url query get    <url> <key>
url query set    <url> <key> <value>
url query delete <url> <key>
```

**Global flags:**

| Flag | Default | Description |
|---|---|---|
| `--json` | off | JSON output where applicable (primarily `parse`). |
| `--raw` | off | Disable normalisation on `build` / `query set`/`delete`. |
| `--mode {component,path,query,form}` | `component` | Encoding variant for `encode` / `decode`. |
| `--form` | off | Shorthand for `--mode form`. Applies to `encode` + `decode`. |
| `--field NAME` | none | (`parse` only) emit a single named field. |
| Standard | | `--describe`, `--help`, `--version`, `--color`, `--no-color`. |

**Subcommand flags:**

- `url build`:
  - `--scheme S` (default `https`)
  - `--host H` (required)
  - `--port N` (optional)
  - `--path P` (optional; slashes preserved, other chars encoded per path mode)
  - `--query K=V` (repeatable; K + V each form-encoded)
  - `--fragment F` (optional; component-encoded)

**Positional + stdin:**

- Subcommands that take a primary input accept either a positional argument or stdin via `-`: `echo "hello" | url encode -`.
- `url build` has no positional input — all from flags.
- No batch mode in v1 — one URL per invocation. Users loop in shell if needed.

**Examples:**

```bash
url encode "hello world"
# hello%20world

url encode "hello world" --form
# hello+world

url decode "hello+world" --form
# hello world

url parse "https://api.example.com:8443/v1/users?q=hello&limit=10#top"
# scheme=https
# host=api.example.com
# port=8443
# path=/v1/users
# query=q=hello&limit=10
# fragment=top

url parse "https://api.example.com/v1" --field host
# api.example.com

url parse "https://x.io/?a=1&b=2&a=3" --json
# {"scheme":"https","host":"x.io","port":null,"path":"/",
#  "query":[{"key":"a","value":"1"},{"key":"b","value":"2"},{"key":"a","value":"3"}],
#  "fragment":null}

url build --host api.example.com --path /v1/users \
          --query q="hello world" --query limit=10
# https://api.example.com/v1/users?q=hello+world&limit=10

url join "https://example.com/blog/" "./2026/post-1"
# https://example.com/blog/2026/post-1

url join "https://example.com/deep/path/" "/login"
# https://example.com/login

url query get "https://x.io/?a=1&b=2" a
# 1

url query set "https://x.io/?a=1" b 2
# https://x.io/?a=1&b=2

url query delete "https://x.io/?a=1&b=2" a
# https://x.io/?b=2
```

**Exit codes:**

| Code | Meaning |
|---|---|
| 0 | Success. |
| 125 | Usage error — bad flags, unknown subcommand, missing required value, base URL not absolute. |
| 126 | Runtime error — invalid URL syntax, key not found for `query get`. |

## Architecture

**Project structure** (mirrors digest/notify):

```
src/Winix.Url/
  UrlOptions.cs              — sealed record carrying parsed subcommand + flags
  SubCommand.cs              — enum: Encode, Decode, Parse, Build, Join, QueryGet, QuerySet, QueryDelete
  EncodeMode.cs              — enum: Component, Path, Query, Form
  ArgParser.cs               — argv → UrlOptions, dispatches on subcommand
  Encoder.cs                 — pure: (string, EncodeMode, form) → encoded
  Decoder.cs                 — pure: (string, EncodeMode, form) → decoded
  UrlParser.cs               — pure: string → ParsedUrl
  UrlBuilder.cs              — pure: parts → string
  UrlJoiner.cs               — pure: (base, relative) → string
  QueryEditor.cs             — pure: (url, op, key, value?) → string
  Formatting.cs              — plain-text and JSON output composition
  ParsedUrl.cs               — record: scheme/userinfo/host/port/path/query/fragment
src/url/
  Program.cs                 — thin orchestrator, subcommand dispatch, exit-code mapping
  url.csproj                 — AOT, PackAsTool
  README.md
  man/man1/url.1
tests/Winix.Url.Tests/
  ArgParserTests.cs
  EncoderTests.cs
  DecoderTests.cs
  UrlParserTests.cs
  UrlBuilderTests.cs
  UrlJoinerTests.cs
  QueryEditorTests.cs
  FormattingTests.cs
```

**Key design points:**

- **One file per pure logic unit.** `Encoder` / `Decoder` / `UrlParser` / `UrlBuilder` / `UrlJoiner` / `QueryEditor` are each ~50-100 lines, independently testable, no I/O.

- **Subcommand dispatch in ArgParser.** `ArgParser.Parse(argv)` returns `UrlOptions` with `SubCommand` enum + the appropriate union fields populated. `Program.cs` is a switch-on-subcommand that calls the right pure function. Keeps orchestration trivial.

- **`ParsedUrl` as the shared record.** Used by `UrlParser.Parse()` output, `UrlBuilder.Build()` input (for constructing from existing pieces), and `QueryEditor` intermediate state. Single canonical shape.

- **Under the hood:** `System.Uri` for parsing + normalisation. `Uri.EscapeDataString` for component encoding; custom escape for path-segment mode. `HttpUtility.ParseQueryString` for query manipulation (preserves order + duplicates via `NameValueCollection`). `new Uri(base, relative)` for `join`.

- **No shared-library additions.** Tool-local code only. No hits on `Winix.Codec` or `Yort.ShellKit` beyond the `JsonHelper` every tool already uses.

- **AOT:** `System.Uri`, `System.Web.HttpUtility`, `System.UriBuilder` are all AOT-safe. `Utf8JsonWriter` via `JsonHelper`. No reflection, no dynamic types.

## Algorithm Choices

### Encoding

| Mode | Behaviour |
|---|---|
| `component` (default) | `Uri.EscapeDataString(input)` — RFC 3986 unreserved set only (`A-Za-z0-9-._~`), space → `%20`. Matches JavaScript `encodeURIComponent`. |
| `path` | Same alphabet as component but `/` preserved. Custom escape function (walks path segments). |
| `query` | Same as component (space → `%20`). Differs from form only on space handling. |
| `form` | Component encoding, then space → `+`. Matches `application/x-www-form-urlencoded`. |

### Decoding

| Mode | Behaviour |
|---|---|
| default (RFC 3986) | `Uri.UnescapeDataString(input)` — literal `+` stays literal. |
| `form` | Replace `+` → space, then `Uri.UnescapeDataString`. |

### Parsing

- `new Uri(input, UriKind.Absolute)` first; on failure, fall back to `UriKind.RelativeOrAbsolute` and emit extractable fields (`scheme`/`host`/`port` become empty).
- Invalid input → exit 126 with `"invalid URL: <reason>"`.
- Plain output: `key=value` lines. `query` emitted as the raw query string (duplicate keys and dots in key names would break any exploded format).
- JSON output: `query` is an array of `{"key":X,"value":Y}` objects — preserves order and duplicates.

### Building

- `UriBuilder` struct constructs the result.
- `--scheme` defaults to `https`; `--host` required.
- `--path` has each segment path-mode-encoded; leading `/` added if missing.
- `--query K=V` values form-encoded (matches most APIs); repeatable; order preserved.
- `--fragment` component-encoded.
- Output normalised unless `--raw` given.

### Joining (RFC 3986 §5)

- `new Uri(new Uri(baseUrl), relativeUrl).ToString()`. .NET's `Uri` implements §5 resolution (dot-segments, absolute-URL override, query-only, fragment-only, protocol-relative).
- Base URL must be absolute — if it isn't, exit 125 `"base URL must be absolute"`.
- Either input invalid → exit 126 `"invalid URL: <reason>"`.

### Query Editing

- `HttpUtility.ParseQueryString(uri.Query)` → `NameValueCollection`.
- `query get`: return first value for key; key absent → exit 126 `"key not found: <k>"`.
- `query set`: replace *all* existing values for key with the given single value; append if absent.
- `query delete`: remove all values for key; no-op if absent (exit 0).
- Result re-serialised via `NameValueCollection.ToString()` (order preserved) and spliced back with `UriBuilder`.

### Normalisation by Subcommand

| Subcommand | Normalised output? |
|---|---|
| `encode` / `decode` | N/A (string-level). |
| `parse` | No — reflects input faithfully. |
| `build` | Yes (unless `--raw`). |
| `join` | Yes (unless `--raw`). |
| `query set/delete` | Yes (unless `--raw`). |
| `query get` | N/A (outputs a value, not a URL). |

## Edge Cases

- **Empty query string (`?`)** — round-trips as `?` on `parse`; dropped by `build`/`join` normalisation unless `--raw`.
- **Duplicate query keys** — `parse --json` preserves order and all values; `query get` returns first match (a `--all` flag could be v2).
- **IDN hosts** — `build`/`join` output Punycode by default; `--raw` preserves Unicode input.
- **Invalid percent-escapes in decode input** (e.g. `%ZZ`) — `Uri.UnescapeDataString` treats as literal `%ZZ`; no error. Documented.
- **Malformed URLs in `parse`** — exit 126 with the `UriFormatException` message as context.
- **UTF-8 in path/query** — standard percent-encoded on the way in; decoded as UTF-8 on the way out.
- **stdin input (`-` positional)** — reads a single line (trailing `\n` stripped). Multi-line input treated as one logical string. If stdin is empty → exit 125 (usage error: input is empty).

## Error Handling

- `UriFormatException` → exit 126, message `"invalid URL: <exception message>"`.
- `ArgumentException` on build (invalid port number, bad host) → exit 125.
- "Key not found" for `query get` → exit 126.
- "Base URL must be absolute" for `join` → exit 125.
- Any other `Exception` → exit 1 with `"url: error: <message>"`.

## JSON Output

Only `url parse` has non-trivial JSON output. Shape:

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

- `null` for absent fields (so consumers can distinguish "no port" from "default port of 0").
- `query` is always an array even with zero or one key — consistent shape across inputs.
- For `url query get` / `build` / `join`, `--json` is accepted but produces a trivial `{"url":"..."}` wrapper (or `{"value":"..."}` for `get`) — kept for suite consistency.

## Testing

- **`EncoderTests`** — per mode, space handling, reserved-char handling, unicode.
- **`DecoderTests`** — per mode, `+` handling, malformed escapes, unicode.
- **`UrlParserTests`** — absolute, relative, each field extracted, duplicate query keys, empty components, `--field NAME` selector.
- **`UrlBuilderTests`** — all options, path encoding, query encoding, default scheme, missing host error, normalisation + `--raw`.
- **`UrlJoinerTests`** — RFC 3986 §5 examples (the spec has 35 normative examples; test the 15 most realistic), base-relative error, invalid-URL errors.
- **`QueryEditorTests`** — get/set/delete, duplicate keys, key-not-found error, ordering preservation, empty query.
- **`FormattingTests`** — plain-text line output, JSON shape.
- **`ArgParserTests`** — subcommand dispatch, flag propagation, Q-matrix (e.g. `--field` without `parse` subcommand → error), unknown subcommand → error.

## Distribution

Pipeline integration follows the digest/notify pattern:

- `bucket/url.json` — scoop manifest
- `.github/workflows/release.yml` — pack/publish/zip/combined-zip/tools-map entries
- `.github/workflows/post-publish.yml` — manifest update + winget generator entries
- `CLAUDE.md` — project layout, NuGet package IDs, scoop manifests list
- NuGet package ID: `Winix.Url`
- Scoop binary: `url.exe`
- **Name collision watch:** `url` is a fish-shell command (a `fish_url_` abstraction), a bash history expansion target, and commonly an environment variable. Unlikely to clash for scoop/winget installs; worth a note in the README.

## Out of Scope (v1)

- `--user` / `--pass` userinfo for `build` — rare, security-discouraged. Add if asked.
- `url query list <url>` — redundant with `url parse --json | jq '.query'`.
- `url query get --all <url> <key>` — defer; first-match is enough for most use cases.
- Batch mode (multi-URL input) — defer; shell loops cover it.
- HTML entity encode/decode — different domain; out of scope.
- IDN with `--raw` flag preserving unicode — shipped by default on parse; build/join default Punycode. `--raw` toggle covered.

## Open Implementation Questions (decide during plan)

- Exact path-segment encoder: iterate segments and call `Uri.EscapeDataString` per segment, or use a custom `Uri.EscapeUriString`-alike? `EscapeDataString` per segment is simpler and more predictable.
- `ArgParser` subcommand dispatch: ShellKit has no built-in subcommand support; each subcommand's flag set is parsed separately. Look at `schedule` tool for the precedent (it has nested subcommands too).
