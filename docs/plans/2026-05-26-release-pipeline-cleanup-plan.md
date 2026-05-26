# Release-pipeline cleanup — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Future Winix releases ship complete, lean artifacts (man pages on every platform incl. `when`, debug symbols split into a separate `-symbols` asset) and `--version` is clean (`X.Y.Z`, no `+gitsha`) across all tools.

**Architecture:** Three source changes — one MSBuild property (`Directory.Build.props`), one csproj line (`src/when/when.csproj`), and a rewrite of the two per-tool zip steps in `.github/workflows/release.yml` from 23 hand-listed lines each into a loop that captures `*.pdb`/`*.dbg` into a `-symbols` zip, strips them from the publish dir, then zips the remainder (recursively, so the `share/man/man1/` subtree is included on *nix). No changes to the release-upload, signing, or post-publish steps — the existing `*.zip` globs carry the new symbols zips through, and the `*-win-x64.zip` globs in `post-publish.yml`/`sign-release.sh` correctly exclude them.

**Tech Stack:** .NET 10 SDK / MSBuild, GitHub Actions (bash + pwsh), Info-ZIP `zip`, PowerShell `Compress-Archive`.

---

## Context the executor needs

Verified facts (from inspecting the *shipped* v0.3.0 release zips, not predictions):

- **`when` is the only tool missing its man-page `<Content Include>`.** 22/23 tool csprojs have a line of the form `<Content Include="man\man1\<tool>.1" CopyToPublishDirectory="PreserveNewest" Link="share\man\man1\<tool>.1" />`; `src/when/when.csproj` does not. This is the reason `when` ships no man page on any platform.
- **The *nix per-tool zip step uses `zip -j <archive> *`** (no `-r`), so `zip` never recurses into the `share/` directory; the man page (which lives at `publish/share/man/man1/<tool>.1`) is silently dropped on Linux/macOS for *every* tool. Windows uses `Compress-Archive`, which recurses, so Windows zips already contain the man page. Fix = recurse on *nix.
- **Symbol sizes (uncompressed, from shipped zips):** Windows native `<tool>.pdb` ≈ 10.4 MB (~85% of the zip); Linux `<tool>.dbg` ≈ 4.7 MB (~64%); macOS has **no** separate native symbol file — only ~41 KB of managed `.pdb`s. Symbol file types to capture: `*.pdb` (all platforms) and `*.dbg` (Linux only). No `.dwarf`/`.dSYM` exists.
- **13 tools emit `X.Y.Z+<gitsha>` from `--version`** because the SDK appends the commit SHA to `AssemblyInformationalVersion`. 9 newer tools strip it in code (their strip becomes a harmless no-op once the SDK stops appending).
- **The combined Windows `winix-win-x64.zip`** is built by a later step (`Create combined Winix zip`) that *overwrites* the per-tool `winix-win-x64.zip` with an exe-only suite bundle + `share/`. Leave that step untouched. Consequence: `winix-win-x64-symbols.zip` contains only the `winix` tool's pdb, which is acceptable (the other 22 tools' symbols ship in their own `-symbols` zips).

The 23 tools, in the order the existing zip steps list them (use this exact order/spelling in the loop arrays):

`timeit squeeze peep wargs files treex man less whoholds schedule winix nc retry when clip ids digest notify url qr protect unprotect envvault`

---

## Task 1: Stop the SDK appending `+gitsha` to the informational version

**Files:**
- Modify: `Directory.Build.props`

- [ ] **Step 1: Record current `--version` output (baseline)**

Run:
```
dotnet run --project src/timeit/timeit.csproj -c Release -- --version
```
Note the output. It may be `0.1.0+<40-hex-sha>` (SHA appended) or `0.1.0` (no SHA — happens when the local build doesn't populate `SourceRevisionId`). Record which.

- [ ] **Step 2: Add the property**

In `Directory.Build.props`, inside the existing top `<PropertyGroup>` (the one starting at line 2 with `<Version>0.1.0</Version>`), add this line after `<RollForward>LatestMinor</RollForward>`:

```xml
    <!-- Don't append "+<commit-sha>" to AssemblyInformationalVersion. Keeps `--version`
         output clean (X.Y.Z) across the whole suite; the 9 tools that strip it in code
         become harmless no-ops. Trade-off: the build commit SHA is no longer embedded in
         assembly metadata (release tag identifies the build instead). -->
    <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
```

- [ ] **Step 3: Rebuild and re-check `--version`**

Run:
```
dotnet run --project src/timeit/timeit.csproj -c Release -- --version
```
Expected: output is `0.1.0` with **no** `+` suffix.

If the Step 1 baseline already had no `+` (local build didn't append the SHA), this step can't observe a difference — note that the property's effect is only visible when CI/SourceLink populates `SourceRevisionId`, and it will be confirmed at the draft-release gate. The change is correct and harmless either way.

- [ ] **Step 4: Guard against a test asserting SHA presence**

Run:
```
```
(Search the test tree for any assertion that the version contains a `+`.)

Use the Grep tool: pattern `InformationalVersion|\+.*[Ss]ha|version.*Contains` across `tests/**/*.cs`. If any test asserts a `+` is present in version output, stop and surface it (per the "test modification = contract change" rule) before proceeding. If none, continue.

- [ ] **Step 5: Commit**

```bash
git add Directory.Build.props
git commit -m "build: stop appending +gitsha to AssemblyInformationalVersion

The SDK appends the commit SHA to AssemblyInformationalVersion by
default, so 13 older tools emitted 'X.Y.Z+<sha>' from --version while 9
newer tools stripped it in code. Setting
IncludeSourceRevisionInInformationalVersion=false fixes all tools at the
source; the in-code strips become no-ops. Trade-off: the build SHA is no
longer in assembly metadata (the release tag identifies the build)."
```

---

## Task 2: Add the missing `when` man-page include

**Files:**
- Modify: `src/when/when.csproj`

- [ ] **Step 1: Add the Content Include**

In `src/when/when.csproj`, after the existing `<ItemGroup>` containing `<None Include="README.md" .../>` (lines 22-24), add a new ItemGroup matching the pattern in `src/timeit/timeit.csproj`:

```xml
  <ItemGroup>
    <Content Include="man\man1\when.1" CopyToPublishDirectory="PreserveNewest" Link="share\man\man1\when.1" />
  </ItemGroup>
```

- [ ] **Step 2: Verify the page copies to publish output**

Run a fast framework-dependent publish (skips the slow AOT compile but still honours `CopyToPublishDirectory`):
```
dotnet publish src/when/when.csproj -c Release -p:PublishAot=false -o tmp/when-publish-check
```
Then confirm the file exists:
```
```
Use the Read or Glob tool to confirm `tmp/when-publish-check/share/man/man1/when.1` exists. Expected: present.

- [ ] **Step 3: Clean up the verification output**

```bash
rm -rf tmp/when-publish-check
```

- [ ] **Step 4: Commit**

```bash
git add src/when/when.csproj
git commit -m "fix(when): package the man page (missing Content Include)

when.csproj lacked the <Content Include man\\man1\\when.1> line that the
other 22 tools have, so when shipped no man page on any platform. The
page source already existed and documents the 'now' keyword; it was just
never copied to publish output."
```

---

## Task 3: Rewrite the Linux/macOS zip step as a symbol-splitting loop

**Files:**
- Modify: `.github/workflows/release.yml` (the `Zip binaries (Linux/macOS)` step, currently ~lines 331-356)

- [ ] **Step 1: Replace the step body**

Replace the entire `Zip binaries (Linux/macOS)` step with:

```yaml
      - name: Zip binaries (Linux/macOS)
        if: runner.os != 'Windows'
        shell: bash
        run: |
          RID="${{ matrix.rid }}"
          TOOLS="timeit squeeze peep wargs files treex man less whoholds schedule winix nc retry when clip ids digest notify url qr protect unprotect envvault"
          for t in $TOOLS; do
            pubdir="src/$t/bin/Release/net10.0/$RID/publish"
            (
              cd "$pubdir"
              # Split debug symbols into a separate artifact: managed .pdb (all platforms)
              # plus native .dbg (Linux). nullglob so a missing pattern expands to nothing
              # rather than a literal glob string. macOS has no native symbol file — only
              # the small managed .pdb files match here.
              shopt -s nullglob
              syms=( *.pdb *.dbg )
              if [ "${#syms[@]}" -gt 0 ]; then
                zip -j "$GITHUB_WORKSPACE/$t-$RID-symbols.zip" "${syms[@]}" > /dev/null
                rm -f "${syms[@]}"
              fi
              shopt -u nullglob
              # Main zip: -r so the share/man/man1/<tool>.1 subtree is included. The previous
              # `zip -j ... *` had no -r and never recursed into share/, dropping every man
              # page on Linux/macOS.
              zip -r "$GITHUB_WORKSPACE/$t-$RID.zip" . > /dev/null
            )
          done
```

- [ ] **Step 2: Syntax-check the bash body**

`zip` is not installed on the dev machine, so the loop can't be run end-to-end locally — but the shell syntax can be validated. Extract just the `run:` script body into `tmp/nix-zip-body.sh` and run:
```
bash -n tmp/nix-zip-body.sh
```
Expected: no output, exit 0 (syntactically valid). Then `rm tmp/nix-zip-body.sh`.

(When extracting, replace the `${{ matrix.rid }}` expression with a literal like `linux-x64` so bash doesn't choke on the GitHub Actions template syntax.)

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "ci: include man pages and split symbols in Linux/macOS zips

The per-tool *nix zip step used 'zip -j ... *' (no -r), which never
recursed into share/, so every tool shipped without its man page on
Linux/macOS. Rewrite as a loop that (1) captures *.pdb/*.dbg into a
separate <tool>-<rid>-symbols.zip, (2) removes them from the publish
dir, (3) zips the remainder recursively so share/man/man1/<tool>.1 is
included. Symbols were ~64% of each Linux download."
```

---

## Task 4: Rewrite the Windows zip step as a symbol-splitting loop

**Files:**
- Modify: `.github/workflows/release.yml` (the `Zip binaries (Windows)` step, currently ~lines 358-384)

- [ ] **Step 1: Replace the step body**

Replace the entire `Zip binaries (Windows)` step with (leave the later `Create combined Winix zip (Windows)` step untouched):

```yaml
      - name: Zip binaries (Windows)
        if: runner.os == 'Windows'
        shell: pwsh
        run: |
          $rid = '${{ matrix.rid }}'
          $tools = @('timeit','squeeze','peep','wargs','files','treex','man','less','whoholds','schedule','winix','nc','retry','when','clip','ids','digest','notify','url','qr','protect','unprotect','envvault')
          foreach ($t in $tools) {
            $pub = "src/$t/bin/Release/net10.0/$rid/publish"
            # Split debug symbols (Windows has native + managed .pdb, no .dbg) into a
            # separate artifact, then remove them so the main zip ships only the binary +
            # man pages.
            $pdbs = Get-ChildItem -Path $pub -Filter *.pdb -File
            if ($pdbs) {
              Compress-Archive -Path $pdbs.FullName -DestinationPath "$t-$rid-symbols.zip" -Force
              Remove-Item $pdbs.FullName -Force
            }
            # Main zip: Compress-Archive recurses the share/ subtree (unchanged behaviour),
            # now pdb-free. The native .pdb was ~85% of each Windows download.
            Compress-Archive -Path "$pub/*" -DestinationPath "$t-$rid.zip" -Force
          }
```

- [ ] **Step 2: Functionally test the pwsh logic locally (synthetic publish dir)**

The dev box is Windows, so this logic *can* be run for real against a fake publish tree. Write `tmp/pwsh-zip-test.ps1`:

```powershell
$ErrorActionPreference = 'Stop'
$root = "tmp/ziptest"
Remove-Item -Recurse -Force $root -ErrorAction SilentlyContinue
$pub = "$root/src/timeit/bin/Release/net10.0/win-x64/publish"
New-Item -ItemType Directory -Force -Path "$pub/share/man/man1" | Out-Null
Set-Content "$pub/timeit.exe" "fake-binary"
Set-Content "$pub/share/man/man1/timeit.1" ".TH timeit 1"
Set-Content "$pub/timeit.pdb" "native-pdb"
Set-Content "$pub/Winix.TimeIt.pdb" "managed-pdb"

Push-Location $root
$rid = 'win-x64'
$tools = @('timeit')
foreach ($t in $tools) {
  $p = "src/$t/bin/Release/net10.0/$rid/publish"
  $pdbs = Get-ChildItem -Path $p -Filter *.pdb -File
  if ($pdbs) {
    Compress-Archive -Path $pdbs.FullName -DestinationPath "$t-$rid-symbols.zip" -Force
    Remove-Item $pdbs.FullName -Force
  }
  Compress-Archive -Path "$p/*" -DestinationPath "$t-$rid.zip" -Force
}
Pop-Location

Write-Host "=== main zip ==="
Expand-Archive "$root/timeit-win-x64.zip" "$root/main-out" -Force
Get-ChildItem -Recurse "$root/main-out" | ForEach-Object { $_.FullName.Substring((Resolve-Path "$root/main-out").Path.Length) }
Write-Host "=== symbols zip ==="
Expand-Archive "$root/timeit-win-x64-symbols.zip" "$root/sym-out" -Force
Get-ChildItem -Recurse "$root/sym-out" | ForEach-Object { $_.FullName.Substring((Resolve-Path "$root/sym-out").Path.Length) }
```

Run:
```
pwsh -NoProfile -File tmp/pwsh-zip-test.ps1
```
Expected:
- main zip contains `timeit.exe` and `share\man\man1\timeit.1`, and **no** `.pdb`.
- symbols zip contains `timeit.pdb` and `Winix.TimeIt.pdb`.

If the assertions hold, `rm -rf tmp/ziptest tmp/pwsh-zip-test.ps1`. If not, fix the step body and re-run.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "ci: split symbols out of Windows zips

Rewrite the per-tool Windows zip step as a loop that captures *.pdb
(native + managed) into a separate <tool>-<rid>-symbols.zip, removes
them from the publish dir, then zips the remainder (Compress-Archive
still recurses the share/ man-page subtree). The native .pdb was ~85% of
each Windows download. The later combined-winix-zip step is unchanged;
winix-win-x64-symbols.zip therefore carries only the winix tool's pdb,
which is acceptable."
```

---

## Task 5: Update tracked known-issues, if any

**Files:**
- Modify (maybe): `docs/known-issues.md`

- [ ] **Step 1: Check whether these defects are tracked**

Use the Grep tool on `docs/known-issues.md` for: `man page`, `pdb`, `symbol`, `gitsha`, `+commit`. 

- [ ] **Step 2: Update or skip**

If any of the three defects (missing man pages, PDB bloat, +gitsha) are listed as open known issues, mark them resolved (note: "fixed for v0.4.0, branch release/v0.4.0") and commit:
```bash
git add docs/known-issues.md
git commit -m "docs: mark man-page/PDB/version-drift issues fixed for v0.4.0"
```
If none are listed, skip this task (no commit).

---

## Final verification (ship gate — performed at v0.4.0 tag time, not during implementation)

These cannot be exercised before a real release. Record them in the v0.4.0 release checklist:

- [ ] After tagging `v0.4.0`, the release is created as a **draft** (`release.yml` `gh release create --draft`). Before running `scripts/sign-release.sh` / publishing, download a sample across all four RIDs:
  - A normal tool, e.g. `timeit-{win-x64,linux-x64,osx-x64,osx-arm64}.zip` → confirm each contains the binary **and** `share/man/man1/timeit.1`, and **no** `.pdb`/`.dbg`.
  - `when-linux-x64.zip` and `when-win-x64.zip` → confirm `share/man/man1/when.1` is now present (regression check for Task 2).
  - `timeit-win-x64-symbols.zip` and `timeit-linux-x64-symbols.zip` → confirm they contain the `.pdb`/`.dbg`.
- [ ] Run `<tool> --version` on a signed win-x64 binary from the draft → confirm no `+sha`.
- [ ] Confirm `scripts/sign-release.sh` still signs correctly: its `*-win-x64.zip` download pattern excludes `*-win-x64-symbols.zip`, so only the real tool zips (with `.exe`) are signed; SHA256SUMS (`*.zip`) will additionally list the symbols zips.

---

## Self-review notes

- **Spec coverage:** Component 1 → Task 2; Component 2 → Task 3; Component 3 → Tasks 3 & 4; Component 4 → Task 1. Out-of-scope items (NuGet pdbs, macOS embedded symbols) deliberately have no task. Verification section maps to the spec's Verification block.
- **No global `DebugType=none`:** confirmed not introduced anywhere — symbols stay *generated*, only relocated at zip time, preserving NuGet/test symbols per Decision A.
- **Type/identifier consistency:** symbols artifact name is `<tool>-<rid>-symbols.zip` in every task and in the ship-gate checks; tool-list spelling/order identical in Tasks 3 and 4.
