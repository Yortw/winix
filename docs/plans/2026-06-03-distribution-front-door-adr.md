# ADR: Distribution Front Door — winget hosts only `winix`, winix owns its own installs

**Date:** 2026-06-03
**Status:** Proposed
**Context:** Winix distributes ~28 tools across NuGet, Scoop, GitHub Releases, and winget. The winget channel submits a manifest *per tool*, manually, as PRs to `microsoft/winget-pkgs` — human-moderated by volunteers the project has no relationship with. Every new tool and every post-release reship spends more of that goodwill, and the project has had to reship many tools after post-release fixes. Roughly half the suite is already published per-tool on winget (Phase 1, at 0.3.0). This ADR changes the distribution *unit* on winget and the install model of the `winix` suite tool to remove the recurring volunteer dependency. It revises decisions in two prior ADRs.
**Related:**
- [Package Manager Publishing ADR](2026-03-30-package-manager-publishing-adr.md) — revises Decisions 2 and 4.
- [winix Installer ADR](2026-04-05-winix-installer-adr.md) — reverses Decision 2; revises Decision 6.

> **Scope note:** This is a decision record only. No implementation (new adapter, manifest schema, release pipeline, or docs/checklist changes) is performed by this ADR. An implementation plan — and the mandatory adversarial plan review — must precede any code.

---

## Decision 1: winget hosts exactly one package — `winix` — as the canonical front door

**Context:** winget is per-package and human-moderated. Submitting and maintaining ~28 packages there means O(tools × reships) volunteer asks. The `winix` suite tool already exists and already discovers tools from a remote manifest, so it can serve as a single entry point that pulls the rest in.

**Decision:** winget carries only the `winix` package. `winget install winix` becomes the **canonical, documented install** for the suite on Windows. Individual tools are *not* discovered or installed through winget going forward.

**Rationale:** Collapses the winget human-cost from O(tools × reships) to roughly O(winix-own-releases), which is rare (winix's binary changes only when winix's own logic changes — not when tools are added or reshipped, because the tool catalogue is a *remote* manifest, see installer ADR Decision 4 and `ManifestLoader.DefaultUrl`). New tools and reships flow entirely through channels the project controls (Scoop bucket + the remote manifest + NuGet), invisible to winget volunteers. Precedent for a single-package-that-installs-more on winget: `winix` is *already* accepted on winget, and bootstrapper-style packages exist there (e.g. `Rustlang.Rustup`).

**Trade-offs Accepted:** Per-tool winget discoverability is lost — searching winget for "tree" won't surface treex. Judged marginal: winget search for common words is already noisy, and the discoverability that matters is the `winix` brand. Install becomes two conceptual steps (`winget install winix` → `winix install <tool>` / `--all`), which is a coherent and expected story for a suite (cf. rustup→cargo, nvm→node).

**Options Considered:**
- *Keep per-tool winget for all 28:* the status quo. Rejected — it is the source of the recurring volunteer dependency this ADR exists to remove.
- *Drop winget entirely, rely on Scoop:* Scoop is fully project-controlled and friction-free, but loses the built-into-Windows discoverability that is winget's main value. Rejected — the front-door package keeps that discoverability at near-zero recurring cost.

## Decision 2: New tools are never submitted to winget per-tool

**Context:** New-package submissions are the expensive, human-heavy winget operation. *(Assumption to verify before relying on it: version bumps to already-accepted packages are substantially more automated than new submissions. If false, it strengthens the case for not maintaining the existing per-tool packages either — see Deferred.)*

**Decision:** No new tool is ever added to winget as its own package. The "When adding a new tool" checklist in `CLAUDE.md` (which currently includes winget manifest generation via `post-publish.yml`) will be revised to drop the per-tool winget step. *(That checklist edit is downstream work, not performed by this ADR.)*

**Rationale:** Directly removes the expensive operation. The front door (Decision 1) gives new tools a reach path without touching winget.

**Trade-offs Accepted:** A temporary, cosmetic inconsistency: ~half the suite has per-tool winget packages (already submitted) and the rest never will. Invisible to users once the canonical documented install is the front door.

**Options Considered:**
- *Finish the remaining ~10 per-tool, then stop:* Rejected — it spends exactly the volunteer goodwill this ADR is trying to stop spending, for packages destined to become legacy side-doors.

## Decision 3: winix gains a self-contained install backend (direct download from GitHub Releases)

**Context:** Every current `IPackageManagerAdapter` wraps an external PM, and `SuiteManager.ExecuteAsync` returns `NoPackageManager` ("no package manager available") when none is present. A winget user who installs `winix` with no Scoop/.NET/Brew therefore hits a dead end on `winix install`. The installer ADR (Decision 2) *rejected* rustup-style direct download specifically to avoid re-implementing download verification, PATH management, and binary placement.

**Decision:** Add a self-contained install backend — a new `IPackageManagerAdapter` (or peer abstraction) that downloads signed native binaries directly from the project's GitHub Releases, verifies them, and places them on PATH. This backend must be usable without any third-party package manager present, so the winget-delivered front door can install the rest of the suite on a bare machine.

**Rationale:** The front-door model (Decision 1) is only viable if winix can install tools without depending on a third-party PM. The original rejection's cost (verification, PATH, placement, security surface) is real but now justified by the front-door payoff, and partly mitigated: the project holds a Certum code-signing cert, so fetched binaries are signed and auditable. `IPackageManagerAdapter` already provides the seam.

**Trade-offs Accepted:** Resurrects exactly the security/effort burden the installer ADR called out — cross-platform download integrity, safe temp-file handling, PATH mutation, and upgrade/uninstall semantics now become winix's responsibility for this backend. This is non-trivial and is the main implementation cost of the whole change. The existing PM adapters (scoop/winget/dotnet/brew) can remain as secondary backends for users who prefer them (see Deferred).

**Options Considered:**
- *Bootstrap Scoop from the winix front door:* have `winix` install Scoop, then install tools via Scoop. Rejected as the primary path — forces a second PM onto winget users who chose winget precisely to avoid managing one; brittle bootstrap chain.
- *Bundle all tools in the winix binary (multi-call / fat binary):* already rejected in installer ADR Decision 1; unchanged here.

## Decision 4: Manifest schema gains per-tool download URL and version

**Context:** `ToolEntry` carries per-PM package IDs but no download URL and no version; the manifest has a single suite `Version`. The self-install backend (Decision 3) needs to know *what to download and which version* per tool. The single suite `Version` is also the lockstep versioning model encoded in data.

**Decision:** Extend the manifest schema so each tool entry can carry a direct-download URL (per RID/platform) and a version, in addition to the existing per-PM package IDs. The release pipeline populates these when it generates `winix-manifest.json`.

**Rationale:** Required to make Decision 3 work. As a bonus, per-tool versions in the manifest are the data foundation for honest per-tool versioning (e.g. `mkauth 0.1.0` rather than inheriting the suite number) and for a Debian-style bill-of-materials where the `winix` package version is the suite handle and the manifest pins each tool — should the project later move off lockstep versioning (see Deferred).

**Trade-offs Accepted:** Schema change requires updating the manifest generator and `ToolManifest.Parse`; older `winix` binaries parsing a newer manifest must ignore unknown fields gracefully (the current parser already skips unknown properties, so forward-compat holds). Per-RID URLs enlarge the manifest, but it remains small.

**Options Considered:**
- *Derive release-asset URLs by convention (tool name + suite version) instead of storing them:* fewer manifest fields, but couples winix to the exact asset-naming scheme and breaks the moment naming changes — the same fragility the installer ADR cited when rejecting the GitHub Releases API. Rejected in favour of explicit URLs.

---

## Decisions Explicitly Deferred

| Topic | Why Deferred |
|-------|-------------|
| Disposition of the ~13 existing per-tool winget packages | Leaning toward "let them ride, not a release gate; deprecate later once the front door has traction." Needs a deliberate call, and depends on verifying whether winget version-bumps for accepted packages are cheap enough to keep them current. Not disruptive to leave as-is for now. |
| Independent (per-tool) vs lockstep suite versioning | Plan is: keep v0.4 lockstep (in progress), then move to individual tool versions afterward. The cutover is a separate decision; Decision 4 lays the data foundation but doesn't mandate the switch. |
| Whether to keep scoop/winget/dotnet/brew adapters as *secondary* backends | The self-install backend (Decision 3) is the primary path for front-door users; the existing adapters can stay for users who prefer a managed PM. Keep-vs-retire is a later call once the self-install backend exists. |
| CLAUDE.md "When adding a new tool" checklist revision | Downstream documentation change (drop per-tool winget step); to be done alongside implementation, not in this ADR. |
| Release pipeline changes (`release.yml` / `post-publish.yml`) | Stop generating per-tool winget manifests; populate manifest URLs/versions. Implementation-plan scope. |
| Verifying winget single-package / bootstrapper policy in current moderation terms | `winix` is already accepted on winget so the precedent holds in practice; a fuller policy check is prudent before relying on it long-term. |
