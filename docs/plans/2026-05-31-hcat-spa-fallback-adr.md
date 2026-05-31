# hcat — `serve --spa` fallback — ADR

**Date:** 2026-05-31
**Status:** Accepted
**Context:** `hcat serve` 404s unmatched paths, so a client-side-routed SPA breaks on deep-link refresh. Related design doc: [`2026-05-31-hcat-spa-fallback-design.md`](2026-05-31-hcat-spa-fallback-design.md).

---

## D1 — Gate the fallback on an explicit `text/html` Accept header

**Context.** When a path matches no file, hcat must decide between returning the SPA shell (so the client router can handle the route) and a real 404 (so a broken asset/API request stays visible).

**Decision.** Fall back to the index file only for `GET`/`HEAD` requests whose `Accept` header **explicitly lists a `text/html` media type**. A bare `*/*` does not qualify; neither does a specific non-HTML type.

**Rationale.** Browsers send `Accept: text/html,...` for navigations and a specific/`*/*` Accept for `fetch`/asset loads, so this cleanly separates "user navigated here" from "code requested a resource." It is the rule that keeps the classic SPA-server footgun — returning `index.html` for a missing `.js`, masking the 404 and breaking debugging — from happening.

**Trade-offs accepted.** A client that deep-links with only `Accept: */*` (e.g. curl, some link checkers) gets a 404 rather than the shell. Judged correct: those are not navigations. `--spa` is opt-in, so the stricter behaviour never affects plain serve.

**Options considered.** *No-file-extension heuristic* — rejected: misfires on extensionless asset routes and on app routes containing a dot (`/users/v1.2`). *Always fall back on any unmatched GET* (miniserve's basic mode) — rejected: serves HTML for missing `.js`/`.css`/API paths, hiding real 404s. *Treat `*/*` as html* — rejected: would hand the shell to curl/API clients.

## D2 — `--spa` disables directory browsing

**Context.** Serve enables automatic directory listings. In SPA mode, an existing directory without an index could either list or fall back.

**Decision.** `--spa` turns directory browsing off. An indexless directory becomes an ordinary unmatched path (navigation → shell, else 404).

**Rationale.** A deployed SPA's routing is owned by the app, not the filesystem; exposing a directory listing both contradicts that model and leaks the build's file tree. One consistent rule (unmatched navigation → shell) is simpler than mixing listings and fallbacks.

**Trade-offs accepted.** A user who wants both a listing and SPA fallback can't have both in one invocation — an unlikely combination.

**Options considered.** *Keep listing on* — rejected: leaks file tree and produces an inconsistent "sometimes a listing, sometimes the shell" model.

## D3 — Opt-in flag with optional custom index; reject silent no-ops

**Context.** How to surface the feature and the fallback filename.

**Decision.** `--spa` (boolean) enables it; `--spa-index <file>` overrides the fallback filename (default `index.html`). `--spa`/`--spa-index` outside serve mode, and `--spa-index` without `--spa`, are usage errors.

**Rationale.** Opt-in keeps plain serve unchanged. `index.html` is the universal SPA entry, so the override is rarely needed but cheap and covers non-standard builds. Rejecting the no-op combinations matches hcat's existing parse-time strictness (`--exit-on body~` in pipe) and prevents a flag silently doing nothing.

**Trade-offs accepted.** A second flag (`--spa-index`) adds a little surface; justified by real non-`index.html` builds and kept inert unless `--spa` is present.

**Options considered.** *`--spa` only, always `index.html`* — viable and simpler, but the user opted to include the override now rather than defer it.

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|---|---|
| Configurable fallback status (e.g. 200 vs 404-with-body) | v1 always 200 — the SPA contract; revisit only with a concrete need. |
| Custom 404 page | Separate feature from SPA fallback; not requested. |
| Fallback for non-GET methods | Navigations are GET/HEAD; other verbs 404 as before. |
| Per-route / multiple fallback rules | Single-shell is the SPA norm; routing tables are framework creep. |
| Caching / ETag tuning on the served shell | Use the file-server defaults; optimise only if asked. |
