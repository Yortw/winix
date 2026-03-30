# Package Manager Publishing — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add scoop and winget publishing to the Winix release pipeline, with scoop bucket in-repo and semi-automated winget manifest generation.

**Architecture:** Scoop manifests live in `bucket/` and are updated by the release workflow directly. Winget manifests are generated as CI artifacts for manual submission. A combined `winix-win-x64.zip` artifact feeds the scoop meta-package. READMEs and CLAUDE.md are updated with all install channels.

**Tech Stack:** GitHub Actions, scoop JSON manifests, winget YAML manifests, bash/jq for CI scripting

---

### Task 1: Create scoop bucket manifests

**Files:**
- Create: `bucket/timeit.json`
- Create: `bucket/squeeze.json`
- Create: `bucket/peep.json`
- Create: `bucket/winix.json`

These are seed manifests with placeholder version/hash. The release pipeline will overwrite version and hash on each release.

- [ ] **Step 1: Create `bucket/timeit.json`**

```json
{
    "version": "0.0.0",
    "description": "Time a command — wall clock, CPU time, peak memory, exit code.",
    "homepage": "https://github.com/Yortw/winix",
    "license": "MIT",
    "architecture": {
        "64bit": {
            "url": "https://github.com/Yortw/winix/releases/download/v0.0.0/timeit-win-x64.zip",
            "hash": "0000000000000000000000000000000000000000000000000000000000000000"
        }
    },
    "bin": "timeit.exe",
    "checkver": "github",
    "autoupdate": {
        "architecture": {
            "64bit": {
                "url": "https://github.com/Yortw/winix/releases/download/v$version/timeit-win-x64.zip"
            }
        }
    }
}
```

- [ ] **Step 2: Create `bucket/squeeze.json`**

```json
{
    "version": "0.0.0",
    "description": "Compress and decompress files using gzip, brotli, or zstd.",
    "homepage": "https://github.com/Yortw/winix",
    "license": "MIT",
    "architecture": {
        "64bit": {
            "url": "https://github.com/Yortw/winix/releases/download/v0.0.0/squeeze-win-x64.zip",
            "hash": "0000000000000000000000000000000000000000000000000000000000000000"
        }
    },
    "bin": "squeeze.exe",
    "checkver": "github",
    "autoupdate": {
        "architecture": {
            "64bit": {
                "url": "https://github.com/Yortw/winix/releases/download/v$version/squeeze-win-x64.zip"
            }
        }
    }
}
```

- [ ] **Step 3: Create `bucket/peep.json`**

```json
{
    "version": "0.0.0",
    "description": "Run a command repeatedly and display output on a refreshing screen.",
    "homepage": "https://github.com/Yortw/winix",
    "license": "MIT",
    "architecture": {
        "64bit": {
            "url": "https://github.com/Yortw/winix/releases/download/v0.0.0/peep-win-x64.zip",
            "hash": "0000000000000000000000000000000000000000000000000000000000000000"
        }
    },
    "bin": "peep.exe",
    "checkver": "github",
    "autoupdate": {
        "architecture": {
            "64bit": {
                "url": "https://github.com/Yortw/winix/releases/download/v$version/peep-win-x64.zip"
            }
        }
    }
}
```

- [ ] **Step 4: Create `bucket/winix.json`**

```json
{
    "version": "0.0.0",
    "description": "Winix CLI tool suite — timeit, squeeze, peep.",
    "homepage": "https://github.com/Yortw/winix",
    "license": "MIT",
    "architecture": {
        "64bit": {
            "url": "https://github.com/Yortw/winix/releases/download/v0.0.0/winix-win-x64.zip",
            "hash": "0000000000000000000000000000000000000000000000000000000000000000"
        }
    },
    "bin": [
        "timeit.exe",
        "squeeze.exe",
        "peep.exe"
    ],
    "checkver": "github",
    "autoupdate": {
        "architecture": {
            "64bit": {
                "url": "https://github.com/Yortw/winix/releases/download/v$version/winix-win-x64.zip"
            }
        }
    }
}
```

- [ ] **Step 5: Commit**

```bash
git add bucket/timeit.json bucket/squeeze.json bucket/peep.json bucket/winix.json
git commit -m "feat: add scoop bucket seed manifests"
```

---

### Task 2: Add combined zip to the publish-aot job

**Files:**
- Modify: `.github/workflows/release.yml` (the `publish-aot` job, lines 94-146)

The win-x64 runner already publishes all three tools. Add a step to create `winix-win-x64.zip` containing all three native binaries.

- [ ] **Step 1: Add combined zip step for Windows**

In `.github/workflows/release.yml`, after the existing "Zip binaries (Windows)" step (line 134-139), add a new step that creates the combined zip. This step only runs on the win-x64 matrix entry:

```yaml
      - name: Create combined Winix zip (Windows)
        if: runner.os == 'Windows' && matrix.rid == 'win-x64'
        shell: pwsh
        run: |
          New-Item -ItemType Directory -Path winix-combined -Force | Out-Null
          Copy-Item src/timeit/bin/Release/net10.0/${{ matrix.rid }}/publish/timeit.exe winix-combined/
          Copy-Item src/squeeze/bin/Release/net10.0/${{ matrix.rid }}/publish/squeeze.exe winix-combined/
          Copy-Item src/peep/bin/Release/net10.0/${{ matrix.rid }}/publish/peep.exe winix-combined/
          Compress-Archive -Path winix-combined/* -DestinationPath winix-${{ matrix.rid }}.zip
```

- [ ] **Step 2: Verify the upload step captures the new zip**

The existing upload step (lines 141-146) uses `path: '*.zip'` which will already pick up `winix-win-x64.zip`. No change needed — just verify this.

- [ ] **Step 3: Attach combined zip to the GitHub Release**

In the `release` job, the `gh release create` command (line 202-206) already attaches `aot/*.zip` which will include the combined zip. No change needed — just verify.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "feat: add combined winix zip to AOT publish step"
```

---

### Task 3: Add scoop bucket update job

**Files:**
- Modify: `.github/workflows/release.yml` (add new job after `release`)

This job runs after the `release` job (so GitHub Release URLs are stable), downloads the win-x64 zips, computes SHA256 hashes, updates the scoop manifests, and pushes to main.

- [ ] **Step 1: Add the `update-scoop-bucket` job**

Append this job to the end of `.github/workflows/release.yml`:

```yaml
  update-scoop-bucket:
    needs: [resolve-version, release]
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          ref: main

      - name: Download AOT binaries (win-x64)
        uses: actions/download-artifact@v4
        with:
          name: aot-win-x64
          path: aot

      - name: Update scoop manifests
        run: |
          VERSION="${{ needs.resolve-version.outputs.version }}"

          update_manifest() {
            local file="$1"
            local zip="$2"
            local hash
            hash=$(sha256sum "$zip" | awk '{print $1}')
            jq --arg v "$VERSION" --arg h "$hash" \
              '.version = $v | .architecture."64bit".hash = $h' \
              "$file" > "${file}.tmp"
            mv "${file}.tmp" "$file"
          }

          update_manifest bucket/timeit.json aot/timeit-win-x64.zip
          update_manifest bucket/squeeze.json aot/squeeze-win-x64.zip
          update_manifest bucket/peep.json aot/peep-win-x64.zip
          update_manifest bucket/winix.json aot/winix-win-x64.zip

      - name: Commit and push scoop bucket
        run: |
          git config user.name "github-actions[bot]"
          git config user.email "github-actions[bot]@users.noreply.github.com"
          git add bucket/
          if git diff --cached --quiet; then
            echo "No scoop manifest changes to commit"
          else
            git commit -m "chore: update scoop manifests to v${{ needs.resolve-version.outputs.version }}"
            git push
          fi
```

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "feat: add scoop bucket update job to release pipeline"
```

---

### Task 4: Add winget manifest generation job

**Files:**
- Modify: `.github/workflows/release.yml` (add new job after `release`)

This job runs after the `release` job, only for stable versions (no `-` in version string). It generates 9 YAML files and uploads them as an artifact.

- [ ] **Step 1: Add the `generate-winget-manifests` job**

Append this job to the end of `.github/workflows/release.yml`:

```yaml
  generate-winget-manifests:
    needs: [resolve-version, release]
    if: ${{ !contains(needs.resolve-version.outputs.version, '-') }}
    runs-on: ubuntu-latest
    steps:
      - name: Download AOT binaries (win-x64)
        uses: actions/download-artifact@v4
        with:
          name: aot-win-x64
          path: aot

      - name: Generate winget manifests
        run: |
          VERSION="${{ needs.resolve-version.outputs.version }}"

          generate_manifests() {
            local tool="$1"
            local tool_title="$2"
            local description="$3"
            local zip="aot/${tool}-win-x64.zip"
            local hash
            hash=$(sha256sum "$zip" | awk '{print $1}' | tr '[:lower:]' '[:upper:]')

            local pkg_id="Winix.${tool_title}"
            local dir="winget-manifests/${pkg_id}/${VERSION}"
            mkdir -p "$dir"

            cat > "${dir}/${pkg_id}.yaml" <<YAML
          PackageIdentifier: ${pkg_id}
          PackageVersion: ${VERSION}
          DefaultLocale: en-US
          ManifestType: version
          ManifestVersion: 1.9.0
          YAML

            cat > "${dir}/${pkg_id}.installer.yaml" <<YAML
          PackageIdentifier: ${pkg_id}
          PackageVersion: ${VERSION}
          InstallerType: zip
          NestedInstallerType: portable
          NestedInstallerFiles:
            - RelativeFilePath: ${tool}.exe
              PortableCommandAlias: ${tool}
          Installers:
            - Architecture: x64
              InstallerUrl: https://github.com/Yortw/winix/releases/download/v${VERSION}/${tool}-win-x64.zip
              InstallerSha256: ${hash}
          ManifestType: installer
          ManifestVersion: 1.9.0
          YAML

            cat > "${dir}/${pkg_id}.locale.en-US.yaml" <<YAML
          PackageIdentifier: ${pkg_id}
          PackageVersion: ${VERSION}
          PackageName: Winix ${tool_title}
          Publisher: Troy Willmot
          License: MIT
          LicenseUrl: https://github.com/Yortw/winix/blob/main/LICENSE
          ShortDescription: ${description}
          PackageUrl: https://github.com/Yortw/winix
          ManifestType: defaultLocale
          ManifestVersion: 1.9.0
          YAML
          }

          generate_manifests "timeit" "TimeIt" "Time a command — wall clock, CPU time, peak memory, exit code."
          generate_manifests "squeeze" "Squeeze" "Compress and decompress files using gzip, brotli, or zstd."
          generate_manifests "peep" "Peep" "Run a command repeatedly and display output on a refreshing screen."

      - name: Upload winget manifests
        uses: actions/upload-artifact@v4
        with:
          name: winget-manifests
          path: winget-manifests/
          retention-days: 90
```

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "feat: add winget manifest generation job to release pipeline (stable only)"
```

---

### Task 5: Update root README.md

**Files:**
- Modify: `README.md` (lines 25-33, the Install section)

- [ ] **Step 1: Replace the Install section**

Replace the existing Install section (lines 25-33) with:

```markdown
## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/timeit    # individual tool
scoop install winix/winix     # all tools
```

### Winget (Windows, stable releases)

```bash
winget install Winix.TimeIt
winget install Winix.Squeeze
winget install Winix.Peep
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.TimeIt
dotnet tool install -g Winix.Squeeze
dotnet tool install -g Winix.Peep
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).
Available for Windows (x64), Linux (x64), and macOS (x64, ARM64).
```

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs: expand root README install section with all package managers"
```

---

### Task 6: Update per-tool READMEs

**Files:**
- Modify: `src/timeit/README.md` (lines 7-10, the Install section)
- Modify: `src/squeeze/README.md` (lines 7-10, the Install section)
- Modify: `src/peep/README.md` (lines 7-10, the Install section)

Each tool README currently has a single `dotnet tool install` line. Replace with all four channels.

- [ ] **Step 1: Update `src/timeit/README.md` Install section**

Replace lines 7-10 with:

```markdown
## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/timeit
```

### Winget (Windows, stable releases)

```bash
winget install Winix.TimeIt
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.TimeIt
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).
```

- [ ] **Step 2: Update `src/squeeze/README.md` Install section**

Replace lines 7-10 with the same pattern, substituting `squeeze` / `Winix.Squeeze`:

```markdown
## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/squeeze
```

### Winget (Windows, stable releases)

```bash
winget install Winix.Squeeze
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.Squeeze
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).
```

- [ ] **Step 3: Update `src/peep/README.md` Install section**

Replace lines 7-10 with the same pattern, substituting `peep` / `Winix.Peep`:

```markdown
## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/peep
```

### Winget (Windows, stable releases)

```bash
winget install Winix.Peep
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.Peep
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).
```

- [ ] **Step 4: Commit**

```bash
git add src/timeit/README.md src/squeeze/README.md src/peep/README.md
git commit -m "docs: add all install channels to per-tool READMEs"
```

---

### Task 7: Update CLAUDE.md

**Files:**
- Modify: `CLAUDE.md` (Conventions section and Project layout section)

- [ ] **Step 1: Add packaging conventions**

After the existing line about NuGet package IDs (line 43 in CLAUDE.md), add:

```markdown
- Scoop bucket: `bucket/` directory contains scoop manifests (`timeit.json`, `squeeze.json`, `peep.json`, `winix.json`). Updated automatically by the release pipeline.
- Winget manifests: generated by the release pipeline for stable versions only (no `-` in version string). Uploaded as `winget-manifests` artifact. Submitted manually to `microsoft/winget-pkgs`.
- When adding a new tool: create a `bucket/{tool}.json` scoop manifest, add the tool's binary to `bucket/winix.json`'s `bin` array, add the tool to the manifest generation step and combined zip step in `.github/workflows/release.yml`, and add install sections to the tool's README.
```

- [ ] **Step 2: Update project layout**

Add `bucket/` to the project layout section in CLAUDE.md:

```
bucket/                    — scoop manifests (updated by release pipeline)
```

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: add packaging conventions and bucket to CLAUDE.md"
```

---

## Plan Self-Review

**Spec coverage check:**
- [x] Section 1 (Publishing policy) — version gating implemented in Task 4 (`if` condition)
- [x] Section 2 (Scoop bucket) — Task 1 (manifests), Task 3 (automation)
- [x] Section 3 (Winget manifests) — Task 4 (generation job)
- [x] Section 4a (Combined zip) — Task 2
- [x] Section 4b (Scoop update job) — Task 3
- [x] Section 4c (Winget generation job) — Task 4
- [x] Section 5 (README updates) — Task 5 (root), Task 6 (per-tool)
- [x] Section 6 (CLAUDE.md updates) — Task 7

**Placeholder scan:** No TBD/TODO found. All code blocks are complete.

**Type consistency:** Package identifiers (`Winix.TimeIt`, etc.), zip names (`timeit-win-x64.zip`, etc.), and scoop names (`timeit`, etc.) are consistent across all tasks.
