# ADR: Package Manager Publishing

**Date:** 2026-03-30
**Status:** Proposed
**Context:** Winix tools are published via NuGet and GitHub Releases. Adding scoop and winget expands reach to Windows users who don't use dotnet tools.
**Related:** [Package Manager Publishing Design](2026-03-30-package-manager-publishing-design.md)

---

## Decision 1: Scoop bucket lives in the winix repo

**Context:** Scoop requires a git repo with a `bucket/` folder. This can be a standalone repo or part of an existing repo.

**Decision:** Host the bucket in the winix repo under `bucket/`.

**Rationale:** Eliminates a second repo, removes the need for a cross-repo PAT, and keeps manifests next to the code. The release workflow already has `contents: write` on the repo.

**Trade-offs Accepted:** Users adding the bucket clone the full winix repo (shallow), but scoop only reads `bucket/*.json` so this is negligible.

**Options Considered:**
- *Separate `Yortw/scoop-winix` repo:* More conventional for scoop buckets, but adds maintenance overhead and requires a PAT for cross-repo pushes. Rejected — not worth the complexity for a small project.

## Decision 2: Winget publishes stable versions only

**Context:** Winget has no release channel or pre-release support. Publishing a `0.2.0-beta.1` to winget makes it appear as a normal release with no user-visible signal that it's pre-release.

**Decision:** Only generate winget manifests when the version string contains no `-` character. NuGet, scoop, and GitHub Releases get all versions.

**Rationale:** Prevents users from unknowingly installing pre-release software via winget. Scoop's Versions bucket convention and NuGet's pre-release flag both handle this natively.

**Trade-offs Accepted:** Early adopters who use winget won't see Winix until the first stable release.

**Options Considered:**
- *Publish all versions to winget:* Simpler pipeline, but misleading to users. Rejected.
- *Use winget's version string to signal pre-release:* Winget doesn't surface this to users. Rejected.

## Decision 3: Package identifiers use `Winix.*` (no publisher prefix)

**Context:** Winget requires a `Publisher.PackageName` identifier. NuGet packages are already published as `Winix.TimeIt`, `Winix.Squeeze`, `Winix.Peep`.

**Decision:** Use `Winix.TimeIt`, `Winix.Squeeze`, `Winix.Peep` for winget — matching NuGet exactly.

**Rationale:** Consistency across package managers. `Winix` works as both publisher and brand. Precedent exists in winget-pkgs for project-name-as-publisher (`Neovim.Neovim`, `GoLang.Go`).

**Trade-offs Accepted:** If another project named "Winix" appeared, there could be a naming collision in winget. Unlikely given the name is distinctive.

**Options Considered:**
- *`Yortw.Winix.TimeIt`:* Groups under GitHub username, but inconsistent with NuGet. Rejected.
- *`TroyWillmot.Winix.TimeIt`:* Personal name as publisher. Unnecessary complexity. Rejected.

## Decision 4: Semi-automated winget submission

**Context:** Winget manifests are submitted as PRs to `microsoft/winget-pkgs`. This can be automated with `wingetcreate` and a PAT, or done manually.

**Decision:** Generate manifests in CI and upload as artifacts. Submit manually for now.

**Rationale:** No stable version exists yet to submit. Automating the PR submission requires a PAT and testing against the real winget-pkgs repo. Not worth the setup cost until there's something to submit regularly.

**Trade-offs Accepted:** Manual step on each stable release until automation is added.

**Options Considered:**
- *Fully automated from day one:* Requires PAT setup, testing, and a stable version to submit. Premature. Deferred.

## Decision 5: Combined `winix` package via zip, not scoop `depends`

**Context:** Scoop's `depends` field could create a meta-package that installs individual tools. Alternatively, a combined zip containing all binaries can be downloaded directly.

**Decision:** The `winix` scoop manifest downloads a combined `winix-win-x64.zip` containing all tool binaries, rather than using `depends` to chain individual installs.

**Rationale:** Scoop's handling of URL-less (depends-only) manifests is unreliable. A combined zip is a single download, simpler to reason about, and doesn't create version coupling issues between individual tool manifests.

**Trade-offs Accepted:** The combined zip duplicates the binaries that are also available individually. Extra build artifact (~few MB). Users who install both `winix` and an individual tool get duplicate binaries on disk.

**Options Considered:**
- *`depends`-only meta-package:* Cleaner conceptually, but scoop may require a URL. Rejected as unreliable.

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|-------|-------------|
| Automated winget PR submission | No stable version exists yet; requires PAT and testing against winget-pkgs |
| Winget `Winix` meta-package | Winget dependency system unreliable with portable installers |
| Chocolatey / MSIX publishing | Lower priority; scoop and winget cover the Windows audience |
| Linux/macOS package managers | Homebrew, apt, etc. are future work; AOT binaries and NuGet cover these platforms for now |
| Scoop submission to community Extras bucket | Own bucket first; submit to Extras once tools have traction |
