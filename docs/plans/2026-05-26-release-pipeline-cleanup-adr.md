# Release-pipeline cleanup — ADR

- **Date:** 2026-05-26
- **Status:** Accepted
- **Related design:** `2026-05-26-release-pipeline-cleanup-design.md`
- **Context:** Inspection of shipped v0.3.0 artifacts revealed missing man pages (Linux/macOS
  on every tool; `when` on all platforms), debug symbols making up 64–85% of each download,
  and a `+gitsha` suffix on `--version` for 13 tools. These decisions cover how the next
  release (v0.4.0) fixes them.

## Decision 1 — Symbols: strip from main zip, publish a separate `-symbols` artifact

1. **Context.** Native symbols are 85% (win) / 64% (linux) of each download. Diagnosability is
   a stated priority (#2), so symbols should remain *available* for crash-dump analysis even if
   not bundled into the default download.
2. **Decision.** Capture `*.pdb`/`*.dbg` into `<tool>-<rid>-symbols.zip`, remove them from the
   publish dir, zip the remainder into the existing `<tool>-<rid>.zip`, and upload both.
3. **Rationale.** Gets the ~85%/64% size win on the artifact users actually download while
   preserving symbolication via a clearly-named companion asset on the same release.
4. **Trade-offs accepted.** Extra release assets (one symbols zip per tool per RID) and slightly
   more pipeline logic. macOS symbols zips carry only ~41 KB of managed pdbs (no separable
   native file) — near-pointless but kept for uniformity.
5. **Options considered.**
   - *Stop generating symbols (`DebugType=none`/`StripSymbols=true`)* — smallest/simplest, but
     permanently loses release-build symbolication; also risks affecting NuGet/test builds.
     Rejected: conflicts with the diagnosability priority.
   - *Exclude from zips, publish nowhere* — no extra assets, but symbols vanish when the CI
     runner recycles. Rejected: same diagnosability loss without the upside of keeping them.

## Decision 2 — Ship the cleanup as part of v0.4.0 (not a v0.3.1 patch)

1. **Context.** Missing man pages are a shipping defect; a v0.3.1 patch would get them to the
   in-flight winget submission sooner.
2. **Decision.** Land on `release/v0.4.0` as one of several v0.4.0 items.
3. **Rationale.** User owns release cadence and is batching v0.4.0 work; a separate patch-release
   cycle is not warranted for these alone.
4. **Trade-offs accepted.** Man pages (incl. `when`) reach winget/scoop users only when v0.4.0
   ships. The v0.3.0 winget submission goes out without the `when` man page.
5. **Options considered.** *v0.3.1 patch branch* — faster fix delivery, but adds a release cycle
   the user did not want. Deferred to user preference, which chose batching.

## Decision 3 — `+gitsha` fix: one suite-wide property, not 13 per-tool edits

1. **Context.** 13 tools emit `X.Y.Z+<sha>`; 9 already strip in code.
2. **Decision.** Add `<IncludeSourceRevisionInInformationalVersion>false</…>` to
   `Directory.Build.props`.
3. **Rationale.** One line fixes all tools at the source (SDK never appends the SHA); the 9
   in-code strippers become harmless no-ops. Avoids 13 near-identical Program.cs edits.
4. **Trade-offs accepted.** Commit SHA no longer embedded in assembly metadata (dump/debug
   tooling can't read the build commit from the binary). Acceptable for a CLI suite where the
   release tag identifies the build.
5. **Options considered.** *Per-tool `GetVersion()` strip on the 13 laggards* — keeps the SHA in
   metadata but is 13× the change and leaves two mechanisms in the tree. Rejected as more work
   for a downside (SHA retention) of little value here.
6. **Amendment (2026-05-26, at implementation).** The premise was empirically falsified: shipped
   v0.3.0 `timeit`/`when` binaries print clean `X.Y.Z` (no SourceLink in the repo, so the SDK
   never appends a SHA). The decision stands but its **status changes from "fix" to
   "forward-guard"** — a no-op today, kept (per user direction) to prevent a future SourceLink
   addition from reintroducing the suffix. Not attributable to any observed defect.

## Decision 4 — Refactor the per-tool zip steps to a loop

1. **Context.** The capture→strip→zip change must apply to 23 tools across two platform steps;
   editing 23×N hand-listed lines is error-prone and is what let the man-page/PDB bugs hide
   per-tool.
2. **Decision.** Replace the two per-tool zip steps with a loop over a tool-name array (bash for
   *nix, pwsh for Windows).
3. **Rationale.** Minimal, DRY way to apply the change uniformly; reduces future per-tool drift.
4. **Trade-offs accepted.** A scripting bug breaks all tools at once rather than one. Mitigated
   by local pwsh testing and the draft-release inspection gate.
5. **Options considered.** *Edit each of the 46 lines in place* — smaller blast radius per line
   but high transcription-error risk and perpetuates the drift smell. Rejected.

## Decision 5 — `when` man page is a single csproj omission, not a class fix

1. **Context.** Prior notes suspected a class of newer tools missing the `<Content Include>`
   man-page line.
2. **Decision.** Add the line to `src/when/when.csproj` only.
3. **Rationale.** Grep of all 23 tool csprojs proved 22 already have it; `when` is the sole gap.
4. **Trade-offs accepted.** None.
5. **Options considered.** *Suite-wide audit/fix* — unnecessary once the grep showed a single
   missing entry; the verification itself closed the "class" hypothesis.

## Decisions explicitly deferred

| Topic | Why deferred |
|---|---|
| Strip NuGet package managed pdbs (~52 KB) | Tiny, and they aid stack-trace symbolication on the JIT tool path; Decision A keeps symbols generated, so no global `DebugType=none`. |
| Strip macOS embedded native symbols (`dsymutil`/`strip`) | No separate symbol file exists; macOS download already ~2.4 MB; more involved for negligible gain. |
| Other v0.4.0 feature work | Out of scope for this cleanup item; tracked separately on the same branch. |
