# ADR: winix — Cross-Platform Suite Installer

**Date:** 2026-04-05
**Status:** Proposed
**Context:** Winix has 6 tools distributed via 4 channels (Scoop, winget, NuGet, GitHub releases). Installing all tools requires multiple commands and varies by platform. A single installer tool would simplify onboarding.
**Related:** [winix Installer Design](2026-04-05-winix-installer-design.md), [Package Manager Publishing ADR](2026-03-30-package-manager-publishing-adr.md)

---

## Decision 1: Suite manager, not multi-call binary

**Context:** The original plan was a BusyBox-style multi-call binary where `winix timeit ...` dispatches to the timeit code. This would provide a single binary containing all tools.

**Decision:** Build a suite manager that installs/updates/uninstalls the individual tools via native package managers. Each tool remains a separate, independently invocable binary.

**Rationale:** `winix timeit --runs 5 cmd` adds noise for no value, especially mid-pipeline where every extra word hurts readability. The real value of "one thing that gets you all the tools" is the installation, not the invocation. A manager provides the single-install experience without degrading the per-tool CLI ergonomics.

**Trade-offs Accepted:** Users still have 6+ separate binaries on disk. Tab completion has more entries. Acceptable — these are independent tools with independent purposes.

**Options Considered:**
- **Multi-call binary (BusyBox-style):** Rejected — adds invocation friction with no benefit over direct tool calls. Piping becomes `winix timeit ... | winix squeeze ...` instead of `timeit ... | squeeze ...`.
- **Both (multi-call + manager):** Rejected — complexity for a dubious feature. Can add multi-call dispatch later if demand exists.

---

## Decision 2: Delegate to native package managers

**Context:** The installer needs to download, verify, and place binaries on PATH. It could implement this directly (like rustup) or delegate to the platform's existing package manager.

**Decision:** Delegate to native PMs (winget, scoop, brew, dotnet tool). `winix` is an orchestrator that shells out to the right PM for the current platform.

**Rationale:** Native PMs already handle download integrity, PATH management, binary placement, upgrade semantics, and uninstall cleanup. Re-implementing this is a large, platform-specific effort with security implications (signature verification, safe temp file handling, PATH mutation). Delegating inherits all of this for free and keeps `winix` focused on its one job: knowing which tools exist and asking the PM to install them.

**Trade-offs Accepted:** Requires a supported PM to be installed. On Linux, this initially means .NET SDK only (for `dotnet tool`). Users without any supported PM get a helpful error, not a fallback installer.

**Options Considered:**
- **Direct download from GitHub releases (rustup-style):** Rejected — requires implementing download verification, PATH management, binary placement, and update mechanics per platform. High effort, security surface area, and maintenance burden.
- **Embed all tools in the `winix` binary:** Rejected — massive binary size, every tool update requires a `winix` release, and still needs a placement/PATH strategy.

---

## Decision 3: Stateless design

**Context:** `winix` needs to know what's installed for `list`, `status`, `update`, and `uninstall`. It could track this in a local state file or query the underlying PM every time.

**Decision:** Stateless — query the PM on every invocation. No local state file.

**Rationale:** A state file can drift from reality if the user installs/uninstalls tools directly via the PM (e.g. `winget uninstall Winix.TimeIt`). The PM is the single source of truth for what it manages. Querying it is reliable and avoids a class of sync bugs. The cost is slightly slower `list`/`status` commands (multiple PM queries), which is acceptable for an infrequent management operation.

**Trade-offs Accepted:** `uninstall` must probe PMs to find which one owns a given tool. If a tool was installed by a PM that's no longer on PATH, `winix` can't uninstall it. Edge case — acceptable.

**Options Considered:**
- **Local state file (`~/.winix/state.json`):** Rejected — state can drift from PM reality, introducing a class of "winix thinks X is installed but it isn't" bugs. The fix (reconciliation logic) adds complexity that exceeds the benefit.

---

## Decision 4: Remote manifest for tool discovery

**Context:** `winix` needs to know what tools exist and their per-PM package IDs. This could be compiled into the binary or fetched from a remote source.

**Decision:** Fetch a JSON manifest from the latest GitHub release (`winix-manifest.json`). The release pipeline generates it automatically.

**Rationale:** A compiled-in manifest means adding a new tool requires releasing a new `winix` binary. A fetched manifest decouples tool discovery from the installer's own release cycle. The manifest is tiny (<1 KB), and management commands are infrequent, so fetch latency is irrelevant.

**Trade-offs Accepted:** Requires network access for every command. No offline mode. Acceptable — you also need network access to actually install packages.

**Options Considered:**
- **Compiled-in manifest:** Rejected — couples tool additions to `winix` releases. Users with an old `winix` wouldn't see new tools.
- **GitHub Releases API (infer from asset names):** Rejected — fragile, couples to naming conventions, slower (API pagination), and GitHub rate limits apply to unauthenticated requests.

---

## Decision 5: `--via` override with platform default chain

**Context:** Multiple PMs may be available on a given machine (e.g. both winget and scoop on Windows). The installer needs to pick one.

**Decision:** Auto-detect available PMs and use a platform-specific preference chain (Windows: winget → scoop → dotnet; macOS: brew → dotnet; Linux: dotnet). User can override with `--via <pm>`.

**Rationale:** Sensible defaults cover most users. The override handles users with strong PM preferences (e.g. preferring scoop over winget). The chain is deterministic and documented.

**Trade-offs Accepted:** If a user has both winget and scoop, `winix install` uses winget. Subsequent `winix install --via scoop` for a specific tool means that tool is managed by a different PM than the rest. Acceptable — the user explicitly asked for it, and the stateless design means `winix` just queries whatever PM has the tool.

**Options Considered:**
- **Require explicit `--via` always:** Rejected — friction for the common case where there's one obvious PM.
- **Persist PM choice after first use:** Rejected — conflicts with stateless design, adds a state file.

---

## Decision 6: Post-install hooks for automatic tool installation

**Context:** On platforms with post-install support, the `winix` installation could automatically install all tools without a second command.

**Decision:** Use Scoop `post_install` and Homebrew `post_install` to run `winix install --via <pm>` after `winix` is installed. Winget and dotnet tool have no hook mechanism; they print a hint instead.

**Rationale:** The primary value of `winix` is "one command to get everything." On platforms where the PM supports it, zero additional commands is even better. The hooks pass `--via` explicitly for deterministic PM selection.

**Trade-offs Accepted:** Post-install failure (network down) leaves the user with `winix` but no tools. Acceptable — `winix install` retries cleanly. The Scoop `winix.json` manifest changes from a combined zip (all binaries bundled) to a single `winix` binary with post-install — this is a breaking change for existing Scoop users upgrading from the combined zip.

**Options Considered:**
- **No post-install hooks:** Rejected — misses the "one command" goal on platforms that support it.
- **Auto-install on first run of any command:** Rejected — surprising behaviour. If the user runs `winix list`, they expect a list, not an install.

---

## Decisions Explicitly Deferred

| Topic | Why Deferred |
|-------|-------------|
| Multi-call dispatch (`winix timeit ...`) | No demonstrated demand; can add later without breaking changes |
| Version pinning (`winix install timeit@0.1.0`) | Over-engineering for v1; latest-only is fine until tools have breaking changes |
| apt/snap/pacman adapters | Linux native PM support adds when there's demand; `dotnet tool` covers Linux for now |
| Homebrew tap + formula creation | Release pipeline concern, not an installer concern; adapter is ready |
| Manifest caching / ETags | Manifest is tiny and commands are infrequent; add if latency becomes a problem |
| Self-update | Updating `winix` via itself creates a "replace while running" problem; use the PM that installed it |
| Offline / air-gapped mode | Requires bundling binaries or a local manifest; out of scope for a PM orchestrator |
