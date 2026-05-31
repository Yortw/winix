# hcat — `serve --spa` fallback

**Date:** 2026-05-31
**Status:** Approved (brainstorm)
**Tool:** `hcat` (`Winix.HCat`)
**Companion ADR:** [`2026-05-31-hcat-spa-fallback-adr.md`](2026-05-31-hcat-spa-fallback-adr.md)

## Problem

`hcat serve` already serves default documents (`index.html` etc.) and 404s unmatched paths. For a client-side-routed single-page app (React/Vue/Angular), deep-linking `/users/42` and refreshing returns **404** — there is no file at that path, so the JS router never loads. This is a real gap for hcat's "preview my built frontend on the LAN" audience; tools like `miniserve` (`--spa`) and npm `serve` offer the fallback, `python -m http.server` does not. Verified current behaviour: an unmatched path returns 404.

## Solution

Opt-in `--spa` mode: an unmatched **browser navigation** returns the app shell (`index.html`, 200) so the client router takes over, while non-navigation misses (assets, API calls) keep their real 404.

### Flags (serve mode only)

- `--spa` — enable SPA fallback.
- `--spa-index <file>` — override the fallback filename (default `index.html`).
- **Validation:** `--spa`/`--spa-index` used with `inspect`/`pipe` → usage error (no meaning there). `--spa-index` without `--spa` → usage error (silent no-op otherwise). Mirrors the existing "reject silent no-ops" rule (e.g. `--exit-on body~` in pipe).
- `HCatOptions` gains `bool Spa` and `string SpaIndexFile` (default `"index.html"`).

### Fallback rule (the correctness crux)

When no real file matched, the fallback serves the index file (`200`, `text/html; charset=utf-8`) **iff all** hold:

1. method is `GET` or `HEAD`;
2. the request `Accept` header **explicitly contains a `text/html` media type** — a bare `*/*` (curl/wget default) or a specific non-HTML type (`application/json`, image types) does **not** qualify, so API clients and asset fetches still get their real 404;
3. the configured index file exists in the served root.

Otherwise the response stays 404. For `HEAD`, headers only (status + `Content-Type` + `Content-Length`), no body.

### Directory browsing

`--spa` **disables** automatic directory browsing (`EnableDirectoryBrowsing = false`). An existing directory without an index is then an ordinary unmatched path: an `Accept: text/html` navigation falls back to the shell, everything else 404s. Keeps the SPA model clean (the app owns routing) and avoids exposing a deployed app's file tree.

## Architecture

All changes are localised to serve-mode wiring + arg parsing.

- **`HCatOptions`** — add `Spa` (bool) and `SpaIndexFile` (string, default `"index.html"`).
- **`ArgParser`** (serve branch) — parse `--spa`/`--spa-index`; enforce the validation rules above.
- **`AcceptsHtml(string? acceptHeader)`** — NEW pure helper (likely in `ServeConfig` or a small static), the testable correctness crux: true iff the header lists an explicit `text/html` media type; false for null/empty, `*/*`-only, or specific non-HTML types. Splits the parsing out of the middleware so it is unit-tested deterministically.
- **`ServeConfig.Apply`** — when `o.Spa`:
  - set `EnableDirectoryBrowsing = false`;
  - after `app.UseFileServer(...)`, register a terminal fallback middleware (runs only on a file-server miss) that applies the fallback rule using the `PhysicalFileProvider` (already built here) + `o.SpaIndexFile`, serving the file via its read stream.
- **Startup nicety** — if `--spa` and the index file is absent from the served root at bind time, print a one-line stderr warning (non-fatal; per-request existence is still checked).

### Middleware ordering (why interactions are already correct)

Serve pipeline order: CI/access-log wrappers → upload-exclusion guard → upload POST receiver → `UseFileServer` (default docs + static [+ browser, off in SPA]) → **SPA fallback (new, terminal)**.

- **Real files win** — fallback is after `UseFileServer`, which is terminal on a match.
- **Upload exclusion wins** — the exclusion guard runs before `UseFileServer` and `return`s (short-circuits) for excluded upload paths, so they stay 404 even in SPA mode; the fallback is never reached for them.
- **Access-log** records the real final status (200 fallback / 404 miss) with no special-casing.
- **Default document** (`/` → `index.html`) is unaffected (`UseDefaultFiles`).

## Testing

- **Unit (`AcceptsHtml`):** `text/html`→true; `text/html,application/xhtml+xml,*/*`→true; `*/*`→false; `application/json`→false; `image/png`→false; null/empty→false.
- **Unit (`ArgParser`):** `--spa-index` without `--spa`→usage error; `--spa` with `inspect`→usage error; `serve --spa`→`Spa` true, default index; `serve --spa --spa-index app.html`→`SpaIndexFile` "app.html".
- **Integration (real Kestrel, `serve --spa`):**
  - GET `/deep/route` with `Accept: text/html` → 200, body is `index.html`.
  - GET `/missing.js` with `Accept: */*` → 404.
  - GET `/api/thing` with `Accept: application/json` → 404.
  - GET `/real.txt` (exists) → 200, the real file (fallback does not override).
  - GET `/` → 200 `index.html` (default document still works).
  - existing subdir without index, `Accept: text/html` → 200 `index.html` (no directory listing emitted).
  - `--spa-index app.html` → fallback serves `app.html`.
  - `serve --upload --spa`, GET `/uploads/x` with `Accept: text/html` → 404 (upload exclusion wins, not the shell).

## Scope

**In:** `--spa` + `--spa-index`; the `AcceptsHtml` helper; the fallback middleware; directory-browsing-off in SPA mode; startup warning; validation; docs (README/man/ai-guide) + the unit/integration tests above.

**Out (YAGNI / deferred):** custom 404 page; configurable fallback status (always 200); `*/*` treated as html; SPA fallback for non-GET; per-route fallback rules; caching headers tuning.
