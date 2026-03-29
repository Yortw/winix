# ADR: Peep Time Machine

**Date:** 2026-03-30
**Status:** Accepted
**Related:** [Design spec](2026-03-30-peep-time-machine-design.md), [Peep ADR](2026-03-29-peep-adr.md)

---

## Decision 1: Zero-cost navigation over persistent panels

### Context

Viddy's time machine uses a permanent side panel (~21 columns) showing a scrollable history list. This works but sacrifices output width — a meaningful cost on typical 80-120 column terminals.

### Decision

Use two navigation modes that consume zero screen space during normal operation:
- **Step mode** (Left/Right arrows) — no UI at all, just header updates
- **History overlay** (t key) — modal popup, dismissed after selection

### Rationale

Peep is a lean, pipe-friendly tool. Users watch command output at full width most of the time. Time machine is used intermittently, not continuously — a modal interaction fits better than a persistent panel.

### Trade-offs Accepted

- No at-a-glance history visibility during normal use (must press `t` to see the list)
- Two-key interaction to jump to a specific snapshot (t → navigate → Enter) vs viddy's always-visible click/scroll

### Options Considered

- **(A) Side panel (viddy-style):** Always visible, but 21-column cost. Rejected — too expensive for peep's terminal-width-sensitive use case.
- **(B) Bottom bar:** Single-line `[3/17]` indicator. Considered but step mode + header achieves the same with zero line cost.
- **(C) Both available (toggle):** Adds complexity for marginal benefit. Rejected — overlay covers the "see full list" use case.

---

## Decision 2: In-memory bounded history

### Context

Need to store command output snapshots for browsing. Options range from pure in-memory to SQLite persistence (like viddy).

### Decision

In-memory `List<Snapshot>` with configurable capacity (default 1000, `--history 0` for unlimited). No persistence across sessions.

### Rationale

- At 2s intervals, 1000 snapshots = ~33 minutes — adequate for most monitoring sessions
- Memory cost is modest: 1000 snapshots x typical 1-50KB output = 1-50MB
- No SQLite dependency keeps the AOT binary lean and avoids temp file cleanup concerns
- Persistence/replay (`--save`/`--load`) can be added later without breaking changes

### Trade-offs Accepted

- Long-running sessions (hours) at short intervals lose oldest history
- No post-session replay capability
- Users must set `--history 0` explicitly if they want unbounded history

### Options Considered

- **SQLite (viddy-style):** Full persistence, replay after exit. Rejected — adds ~1MB+ dependency, temp file cleanup complexity, and crash-orphan concerns. Overkill for peep's typical use.
- **Temp files, clean on exit:** More history capacity, but crash = orphaned files. Rejected — worse reliability story than in-memory for marginal benefit.
- **Unbounded in-memory by default:** Simpler, but a `peep` session running overnight watching a verbose command could consume GB of RAM silently. Rejected — bounded default is safer.

---

## Decision 3: Left/Right for time stepping, t for overlay

### Context

Need keybindings for time machine navigation that don't conflict with existing bindings (Space=pause, Up/Down=scroll, d=diff, ?=help, r/Enter=rerun, q=quit).

### Decision

- `Left/Right` arrows for stepping through history (timeline metaphor)
- `t` for history overlay
- Time machine auto-pauses on entry; Space/Esc/Right-at-newest exits back to live

### Rationale

Left/Right are unused and have a natural "timeline" association. `t` is mnemonic for "time". Auto-pause on entry is the only sane behaviour — you can't browse history while the display is updating.

### Trade-offs Accepted

- Left/Right are unavailable for future horizontal scroll if output is wider than terminal (unlikely need — truncation is standard for `watch`-like tools)

### Options Considered

- **Shift+J/K (viddy-style):** Vim-ish but not discoverable. Rejected — peep uses plain keys, not Shift combos.
- **[/] brackets:** Available but no strong mnemonic. Left/Right is more intuitive.

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|-------|-------------|
| Persistence / replay (`--save`, `--load`) | No demand yet; in-memory is sufficient for v1. Data model supports adding this later. |
| Jump to snapshot at specific time | No UI mechanism — would need a text input prompt. Data structure supports binary search on timestamps. |
| Snapshot compression / deduplication | Premature optimisation. Most sessions won't hit memory limits. |
| Multi-call binary integration | Waiting for umbrella `winix` binary design. |
