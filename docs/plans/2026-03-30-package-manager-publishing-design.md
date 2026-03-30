# Package Manager Publishing — Design Spec

**Date:** 2026-03-30
**Status:** Approved

## Overview

Add scoop and winget publishing to the Winix release pipeline, alongside the existing NuGet and GitHub Release channels.

**Goals:**
- Publish all tools to a self-hosted scoop bucket (in this repo) for Windows users
- Generate winget manifests for stable releases (semi-automated submission)
- Produce a combined `winix` package for scoop (all tools in one install)
- Update READMEs with installation instructions for all channels
- Update CLAUDE.md so new tools get all packaging automatically

**Non-goals:**
- Automated winget PR submission (future — design for it, don't build it)
- Chocolatey, MSIX, or other package managers
- Linux/macOS package managers (homebrew, apt, etc.)

## 1. Publishing Policy

| Channel              | Pre-release (version has `-`) | Stable (no `-`) | Package identifiers                              |
|----------------------|-------------------------------|-----------------|---------------------------------------------------|
| GitHub Release       | Yes                           | Yes             | —                                                 |
| NuGet (dotnet tool)  | Yes                           | Yes             | `Winix.TimeIt`, `Winix.Squeeze`, `Winix.Peep`    |
| Scoop (own bucket)   | Yes                           | Yes             | `timeit`, `squeeze`, `peep`, `winix`              |
| Winget               | No                            | Yes             | `Winix.TimeIt`, `Winix.Squeeze`, `Winix.Peep`    |

**Version gating:** The release pipeline checks whether the version string contains a `-` character. Winget manifest generation is skipped for pre-release versions. All other channels always publish.

## 2. Scoop Bucket

### Location

The bucket lives in the winix repo itself under `bucket/`. No separate repository needed, no PAT required — the release workflow already has `contents: write` permission.

### User experience

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/timeit          # individual tool
scoop install winix/winix           # all tools
scoop update winix/timeit           # update individual tool
```

### Manifests

Four JSON files in `bucket/`:

**Individual tool manifest** (e.g. `bucket/timeit.json`):

```json
{
    "version": "0.1.0",
    "description": "Time a command — wall clock, CPU time, peak memory, exit code.",
    "homepage": "https://github.com/Yortw/winix",
    "license": "MIT",
    "architecture": {
        "64bit": {
            "url": "https://github.com/Yortw/winix/releases/download/v0.1.0/timeit-win-x64.zip",
            "hash": "<sha256>"
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

**Combined meta-package** (`bucket/winix.json`):

Downloads a combined zip containing all three binaries:

```json
{
    "version": "0.1.0",
    "description": "Winix CLI tool suite — timeit, squeeze, peep.",
    "homepage": "https://github.com/Yortw/winix",
    "license": "MIT",
    "architecture": {
        "64bit": {
            "url": "https://github.com/Yortw/winix/releases/download/v0.1.0/winix-win-x64.zip",
            "hash": "<sha256>"
        }
    },
    "bin": ["timeit.exe", "squeeze.exe", "peep.exe"],
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

### Automation

The release pipeline updates the bucket directly:
1. Compute SHA256 hashes of win-x64 zips (individual + combined)
2. Update `version` and `hash` fields in each `bucket/*.json`
3. Commit and push to main

No external PAT needed — uses the workflow's built-in `GITHUB_TOKEN`.

## 3. Winget Manifests (Semi-Automated)

### Package identifiers

`Winix.TimeIt`, `Winix.Squeeze`, `Winix.Peep` — matching the NuGet package IDs. The `Publisher` field in the locale manifest is "Troy Willmot".

### Manifest format

Winget uses a multi-file YAML structure. For each tool, three files:

**Version manifest** (`Winix.TimeIt.yaml`):

```yaml
PackageIdentifier: Winix.TimeIt
PackageVersion: 0.1.0
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.9.0
```

**Installer manifest** (`Winix.TimeIt.installer.yaml`):

```yaml
PackageIdentifier: Winix.TimeIt
PackageVersion: 0.1.0
InstallerType: zip
NestedInstallerType: portable
NestedInstallerFiles:
  - RelativeFilePath: timeit.exe
    PortableCommandAlias: timeit
Installers:
  - Architecture: x64
    InstallerUrl: https://github.com/Yortw/winix/releases/download/v0.1.0/timeit-win-x64.zip
    InstallerSha256: <sha256>
ManifestType: installer
ManifestVersion: 1.9.0
```

**Locale manifest** (`Winix.TimeIt.locale.en-US.yaml`):

```yaml
PackageIdentifier: Winix.TimeIt
PackageVersion: 0.1.0
PackageName: Winix TimeIt
Publisher: Troy Willmot
License: MIT
LicenseUrl: https://github.com/Yortw/winix/blob/main/LICENSE
ShortDescription: Time a command — wall clock, CPU time, peak memory, exit code.
PackageUrl: https://github.com/Yortw/winix
ManifestType: defaultLocale
ManifestVersion: 1.9.0
```

### Automation (semi-automated)

- The release pipeline generates all 9 YAML files (3 per tool) with correct versions, URLs, and SHA256 hashes
- Files are uploaded as a `winget-manifests` artifact on the GitHub Release
- Manual submission to `microsoft/winget-pkgs` via `wingetcreate` or file copy

### Future automation

Replace with a `wingetcreate` step that auto-submits PRs to `microsoft/winget-pkgs`. Will require a `WINGET_GITHUB_PAT` secret at that point. Not implemented now.

### No winget meta-package

Winget's dependency system is unreliable with portable installers. Individual packages only.

## 4. Release Pipeline Changes

### 4a. Combined zip artifact

In the `publish-aot` job, the win-x64 runner creates an additional `winix-win-x64.zip` containing all three native binaries (`timeit.exe`, `squeeze.exe`, `peep.exe`). This feeds the scoop `winix` meta-package and is attached to the GitHub Release.

### 4b. New job: `update-scoop-bucket`

- **Depends on:** `release` (needs the GitHub Release to exist so zip URLs are stable)
- **Runs on:** `ubuntu-latest`
- **Runs for:** all versions (stable + pre-release)
- **Steps:**
  1. Checkout winix repo
  2. Download win-x64 AOT zips (individual + combined)
  3. Compute SHA256 hashes
  4. Update `version` and `hash` in each `bucket/*.json` using `jq` or `sed`
  5. Commit and push to main
- **Token:** uses built-in `GITHUB_TOKEN` (already has `contents: write`)

### 4c. New job: `generate-winget-manifests`

- **Depends on:** `release`
- **Runs on:** `ubuntu-latest`
- **Runs for:** stable versions only (gated on version not containing `-`)
- **Steps:**
  1. Download win-x64 AOT zips
  2. Compute SHA256 hashes
  3. Generate 9 YAML files from templates (3 per tool)
  4. Upload as `winget-manifests` artifact

### Existing `release` job change

Attach the additional `winix-win-x64.zip` to the GitHub Release alongside the existing per-tool zips.

## 5. README Updates

### Root README.md

Expand the Install section to cover all channels:

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
```

### Per-tool READMEs

Each tool README (`src/timeit/README.md`, etc.) gets a matching Install section with all four channels, specific to that tool.

## 6. CLAUDE.md Updates

Add packaging conventions to the Conventions section so new tools get the full treatment:

- **Scoop manifests:** each tool has a `bucket/{tool}.json` manifest. When adding a new tool, create the manifest and add the tool's binary to the `winix.json` combined manifest's `bin` array.
- **Winget manifests:** generated by the release pipeline from templates. When adding a new tool, add the tool's metadata to the manifest generation step in `release.yml`.
- **Combined zip:** the `winix-win-x64.zip` includes all tool binaries. When adding a new tool, add it to the combined zip step in `release.yml`.
- **README install sections:** each tool README and the root README list all install channels. When adding a new tool, follow the existing pattern.

Update the project layout section to include `bucket/`.

## 7. New Secrets

| Secret              | Purpose                              | When needed     |
|---------------------|--------------------------------------|-----------------|
| `WINGET_GITHUB_PAT` | Auto-submit PRs to `winget-pkgs`     | Future (not now)|

No new secrets required for the initial implementation.

## File Map

| File | Action |
|------|--------|
| `bucket/timeit.json` | Create — scoop manifest |
| `bucket/squeeze.json` | Create — scoop manifest |
| `bucket/peep.json` | Create — scoop manifest |
| `bucket/winix.json` | Create — scoop combined manifest |
| `.github/workflows/release.yml` | Modify — add combined zip, scoop update job, winget generation job |
| `README.md` | Modify — expand Install section |
| `src/timeit/README.md` | Modify — add Install section |
| `src/squeeze/README.md` | Modify — add Install section |
| `src/peep/README.md` | Modify — add Install section |
| `CLAUDE.md` | Modify — add packaging conventions, update project layout |

## Manual Steps After Implementation

1. **First stable release:** after pushing a tag without `-`, download the `winget-manifests` artifact and submit to `microsoft/winget-pkgs`
2. **Test scoop install:** after first release, verify `scoop bucket add winix https://github.com/Yortw/winix && scoop install winix/timeit` works
