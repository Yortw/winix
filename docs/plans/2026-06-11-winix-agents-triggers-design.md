# winix agents — trigger-payload rework — design

**Date:** 2026-06-11
**Status:** Approved (brainstorm complete; pending implementation plan)
**Surface:** `AgentsManager.RenderBlock` in `src/Winix.Winix/AgentsManager.cs` (the marker-delimited block `winix agents init` writes into a project/user `AGENTS.md` / `CLAUDE.md`).
**Companion ADR:** `2026-06-11-winix-agents-triggers-adr.md`
**Builds on:** `2026-06-08-winix-agents-design.md` / `-adr.md` (the original feature). This reworks the block's **payload only** — no change to the `init`/`remove`/`status` plumbing, scope handling, marker contract, version pinning, or atomic write.

## 1. Problem

The `winix agents` feature exists to make the suite discoverable to an AI agent working in a project (or on a machine) where Winix is merely installed — the cold-start case an agent cannot otherwise solve, because it does not enumerate installed CLI tools and has no prior that Winix exists.

The original ADR **Decision 2** set the right bar: the injected block should *"carry behaviour-changing essentials inline"*, and its accepted trade-off was that *"the inline core must stay small but **complete enough to change behaviour alone**."* The **shipped payload does not meet that bar.** What `RenderBlock` currently emits is:

- a generalised governance paragraph ("prefer a Winix tool only when it's genuinely the better choice… if you can't say why it beats the default, use the default"), and
- pointers to *pull* detail at decision time: `winix list`, `<tool> --describe`, and a URL.

This is **behaviourally inert** for two structural reasons:

1. **Goal-restating, not signal-giving.** "Use the best tool" repeats a goal the agent already holds. It provides no new *signal* and no recognisable *moment* to act — so it changes nothing.
2. **The discovery path is pull, and agents don't pull.** "Run `winix list` then `--describe` each tool" models a deliberative agent that pauses mid-task to enumerate a 28-tool suite. Real agents act from priors + already-loaded context; they do not run a discovery sweep, so the named tools stay invisible.

The net effect: an agent receiving the current block learns *that* Winix exists and *that* it should be conservative — but never *when* to reach for a specific tool. The single most useful situational content that does exist (per-tool `.Platform` `value_on_windows`/`value_on_unix` statements; the repo `AGENTS.md` Windows-gap list) lives only on the pull path. This is the same failure that motivated the `online` discoverability discussion: a well-built tool that an agent will not reach for because nothing in always-loaded context *triggers* it.

## 2. The fix, in one sentence

Replace the inert governance-plus-pull-pointer payload with a small, **curated, situation-keyed trigger list** — push, not pull — that names the high-value tools at the moment an agent would recognise it needs them, while keeping a one-line conservative guard and delegating the *full* catalogue to `winix list` (unchanged).

## 3. Curation principle (how tools were selected)

A tool earns a scarce line **only** when there is a *recognisable situation* in which (a) the output feeds the agent's next decision, or is a deliverable the agent needs, **and** (b) the agent's default move is *genuinely worse* — brittle, wrong, destructive, a privacy leak, or "give up" — not merely "slightly nicer."

Three composable filters implement that test:

1. **Consumer lens — exclude tools whose value is presentation to a human's eyes.** An agent reads files directly, does not watch a refreshing screen, does not page. Removes `treex`, `peep`, `less`, `man` (and `qr`'s *rendering*, though `qr` re-enters under the privacy angle below).
2. **No native duplication — exclude tools that duplicate a capability the agent already has.** `files` ≈ Glob/Grep, `wargs` ≈ the agent's own orchestration. The agent is *not blocked* without these, so a trigger is noise — and a loose one would actively lure it off the faster native path. (They re-enter narrowly in the footer; see §5.)
3. **Default-is-genuinely-worse — the (a)/(b) bar above.** This keeps the tools where the alternative is a brittle workaround, a wrong result, a destructive default, or a data leak.

A **CI/scripting-heavy** heuristic reinforces inclusion: tools whose primary purpose is unattended scripting/automation (`online`, `retry`, the future try/catch tool) are in by default — they are squarely "agent consumes, default is worse."

### Two kinds of "generic"

A late refinement (it shapes the footer in §5): there are two kinds of generic guidance, and only one is inert.

- **Goal-restating generic** ("use the best tool") — inert; repeats a held goal, gives no signal.
- **Symptom-triggered generic** ("if you observe *symptom X*, check for a Winix tool") — keyed to an *observable event*, so it can change behaviour even without naming a tool.

The footer's general-signal rule (§5) is deliberately the second kind.

## 4. The binding constraint: scannability, not completeness

The block is loaded into an agent's **always-loaded context** every session. Every line competes for the same attention budget against the entire system prompt and the project's own `CLAUDE.md`. A long block **recreates the dilution problem it cures** — the agent skims and tunes out. Therefore the design target is **a tight, scannable block (~6 situation bullets; on the order of 12–18 physical lines including markers and footer), optimised for scarcity over completeness.** Curation is bounded by this budget: tools are clustered *by situation* so one line covers several tools, and marginal tools are dropped or demoted to the footer rather than spending a scarce trigger.

## 5. The block content

Two render modes already exist (`UserScope` asserts availability; `ProjectScope` uses conditional "if installed" wording). Only the **lead sentence** and the **authority phrasing** differ between modes; the trigger bullets and footer are identical. Below is the **user-scope** rendering; the project-scope differences follow.

### User-scope block

```markdown
## Winix CLI tools (available on this machine)

Prefer a winix tool only when it genuinely beats the platform default — otherwise use the default. It does when:

- **A network op or command is flaky** — `online` blocks until connectivity (or a named endpoint) is healthy so you resume instead of polling/giving up; `retry` re-runs a failing command with backoff; `nc` checks whether a port is open.
- **You're about to delete files** — `trash` removes them recoverably instead of `rm`.
- **You need to know what's locking a file or holding a port** — `whoholds` (no native cross-platform equivalent).
- **Building an authenticated request, or handling secrets** — `mkauth` (OAuth1/JWT/Basic/Azure headers), `digest` (hash/HMAC), `envvault` (keychain-backed env vars), `protect`/`unprotect` (encrypt-at-rest), `mksecret` (generate passwords/keys).
- **You need a modern ID** — `ids` (UUIDv4/v7, ULID, NanoID).
- **Acting for the user / keeping data local** — `notify` (ping them when a long task finishes or while away), `qr` (hand a URL to their phone — don't POST it to a web QR service), `clip` (copy to their clipboard, not a pastebin).

Conventions: every tool has `--describe` (JSON schema) and `--json`; exit 0 = success; `NO_COLOR` honored.
General signal: if a command fails from Windows path-mangling, or you're hand-parsing text a tool emits
for humans, a winix tool may fit (Windows-native, `--json`) — e.g. `files`/`wargs` over `find`/`xargs`;
`winix list` for the rest. No clear win? Keep the default. Full guidance:
https://github.com/Yortw/winix/blob/v{version}/AGENTS.md
```

### Project-scope differences

- **Header:** `## Winix CLI tools (if available in your environment)` (existing wording).
- **Lead:** conditional — "If Winix tools are installed in your environment, prefer one only when it genuinely beats the platform default — otherwise use the default. If Winix is not installed, ignore this section. It does when:" (preserves the existing project-scope "ignore if not installed" guard).
- **Trigger bullets + footer:** identical to user-scope.

### Two groups + footer, mapped to the curation principle

- **Group 1 — "your task hit this → reach for it"** (filters 1–3; agent consumes; default genuinely worse): the network/flaky line (`online`/`retry`/`nc`), `trash`, `whoholds`, the auth/secrets line (`mkauth`/`digest`/`envvault`/`protect`/`unprotect`/`mksecret`), `ids`. Six tools' worth of capability folded into five situation bullets.
- **Group 2 — "keep it local / offer to the user"** (human consumes, but **privacy** or **proactive-offer** value clears the (a)/(b) bar): `notify` (a capability the agent wouldn't web-search for — "you can ping the user out-of-band"), `qr` (don't leak a payload to a web QR service; offer for phone hand-off), `clip` (keep off remote paste services). One bullet, explicitly framed around *keep data local / offer*.
- **Footer** — conventions + the **symptom-triggered general-signal rule** carrying its own concrete example (`files`/`wargs` over `find`/`xargs`), the long-tail pull fallback (`winix list`), the **anti-over-reach guard** ("No clear win? Keep the default"), and the version-pinned URL. This is where `files`/`wargs` and the path-mangling/text-parsing meta-rule live — a half-sentence, not a scarce trigger.

### Excluded, and why

| Tool(s) | Filter | Why out of the trigger list |
|---|---|---|
| `treex`, `peep`, `less`, `man` | 1 (consumer = human eyes) | Presentation tools; an agent reads files / `--describe` directly. |
| `files`, `wargs` | 2 (native duplication) | Agent has Glob/Grep + native orchestration; real but narrow win (structured metadata, Windows-safe scripts) → **footer half-clause**, not a trigger, to avoid luring the agent off the faster native path. |
| `timeit` | 3 (weak) | "Measure a command" is an uncommon agent situation; no scarce line. Covered by `winix list` for the rare case. |
| `schedule`, `squeeze`, `hcat`, `demux`, `when`, `url` | 3 (situation not common/recognisable enough to spend a scarce line) | Real tools, but their triggering situation is rare or not sharply recognisable; delegated to `winix list` / `--describe`. (`url`/`when` are genuine capability gaps but low-frequency; revisit if the budget grows.) |
| `winix` | — | The installer itself; meta, not a task tool. |

> Curation note: inclusion is bounded by the §4 budget, not by "passes the bar." `url`, `when`, `schedule`, `squeeze`, `hcat`, `demux` arguably pass the (a)/(b) bar in their niches but lose the *scarce-line* contest to higher-frequency, sharper-trigger tools. The full catalogue remains reachable via `winix list`; the trigger list is intentionally a high-signal subset, not an index.

## 6. What changes in the code

- **`AgentsManager.RenderBlock(string version, RenderMode mode)`** — rewrite the body content (the `lead`, the bullet lines, the footer) to the §5 content. The method signature, the marker lines, the `v={version}` stamp, the LF-only emission contract, and the user/project mode split are **unchanged**.
- **Nothing else in `AgentsManager` changes** — `MergeBlock`, `RemoveBlock`, `FindBlockVersion`, `RunInit`/`RunStatus`/`RunRemove`, scope/target resolution, the atomic `WriteAllText`, and the `IAgentsFileSystem` seam are all untouched. The block stays manifest-decoupled: the trigger list is **static hand-curated text**, rendered from the version string alone (consistent with the original "no manifest coupling / no embedded full list" deferral — a curated *handful of triggers* is not the *full enumeration* that deferral ruled out).

## 7. Testing

- **`RenderBlock` content assertions** (extend the existing `AgentsManager` unit tests):
  - **Required triggers present** (user + project mode): the block contains each Group-1/Group-2 tool name that earns a line (`online`, `retry`, `nc`, `trash`, `whoholds`, `mkauth`, `digest`, `envvault`, `protect`, `unprotect`, `mksecret`, `ids`, `notify`, `qr`, `clip`) and the footer `files`/`wargs` clause.
  - **Excluded tools absent as triggers** (the invariant that prevents drift back to a dump): assert the block does **not** name `treex`, `peep`, `less`, `man` (negative/requirement test — pins the curation, not just the mechanism).
  - **Governance guard retained**: the block still contains the "otherwise use the default" / "No clear win? Keep the default" conservative clause (so the rework doesn't swing into cargo-cult-winix).
  - **General-signal rule present**: the footer contains the path-mangling / text-parsing symptom line.
  - **Budget bound**: assert the rendered block is ≤ a fixed line ceiling (e.g. ≤ 20 lines incl. markers) — a regression guard that keeps future edits honest to §4's scannability discipline.
  - **Both modes**: user-scope asserts availability wording; project-scope asserts the conditional "if … installed … ignore this section" lead. Markers + `v=` stamp identical in both.
  - **Version pinning unchanged**: the URL still pins `v{version}` for stable and `main` for pre-release (existing `UrlRef` behaviour — re-assert it survives the rewrite).
- **Idempotence/merge unchanged**: existing `MergeBlock`/`RemoveBlock`/drift tests must still pass byte-stable at the new content (re-running `init` at the same version is a no-op).
- **Doc↔behaviour reconciliation**: the repo `AGENTS.md` "How to discover" / "honest framing" prose should be checked for consistency with the new block (the block now *names* tools inline rather than only delegating to `--describe`); update the `AGENTS.md` narrative if it now contradicts the block. (Scope: prose alignment only — the canonical detail stays in `AGENTS.md` behind the URL.)

## 8. Deferred / out of scope

| Topic | Why deferred |
|---|---|
| Generating the trigger list from per-tool `.Platform` fields | Reintroduces manifest coupling the original ADR deferred; static curation is the consistent choice. Revisit only if hand-maintenance proves error-prone. |
| Per-tool trigger lines for `url`/`when`/`schedule`/`squeeze`/`hcat`/`demux` | Pass the bar in niche, lose the scarce-line budget contest; reachable via `winix list`. Revisit if the budget or usage data warrants. |
| Reworking the repo `AGENTS.md` "honest framing" prose itself | The block is the always-loaded surface that matters; the repo file is the behind-the-URL detail. Beyond a consistency pass (§7), a full rewrite is separate scope. |
| A hook that runs the highest-value tools (e.g. `online`) deterministically | A stronger mechanism than advice for the top cases, but it's harness/settings work, not a `winix agents` change. Separate effort. |
| Updating the original `2026-06-08-winix-agents-design.md` | Left as the historical record; this doc supersedes its §"block content" only. |
