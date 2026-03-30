# Release Pipeline — Design Spec

**Date:** 2026-03-30
**Status:** Approved

## Overview

Add release infrastructure to ship Winix tools as NuGet tool packages and AOT native binaries via GitHub Releases.

**Goals:**
- Publish all three tools to NuGet.org as `dotnet tool install` packages
- Publish AOT native binaries for 4 platforms as GitHub Release assets
- Tag-based release trigger with manual dispatch fallback
- Proper NuGet metadata (license, description, README, repo URL)

**Non-goals:**
- winget/scoop/msix manifests (future — AOT binaries are the prerequisite)
- Multi-call binary
- Automated changelog generation

## 1. MIT License File

Add `LICENSE` at repo root. Standard MIT license text with copyright holder "Troy Willmot".

## 2. NuGet Package Metadata

### Shared properties (Directory.Build.props)

Add to the existing `Directory.Build.props`:

```xml
<Authors>Troy Willmot</Authors>
<Copyright>Copyright (c) Troy Willmot</Copyright>
<PackageLicenseExpression>MIT</PackageLicenseExpression>
<RepositoryUrl>https://github.com/Yortw/winix</RepositoryUrl>
<RepositoryType>git</RepositoryType>
<PackageProjectUrl>https://github.com/Yortw/winix</PackageProjectUrl>
<PackageTags>cli;tools;cross-platform;aot</PackageTags>
```

### Per-tool properties (each tool csproj)

Each console app csproj gets:

```xml
<PackageId>Winix.TimeIt</PackageId>
<Description>Time a command — wall clock, CPU time, peak memory, exit code. A cross-platform 'time' replacement.</Description>
<PackageReadmeFile>README.md</PackageReadmeFile>
```

Plus an `<ItemGroup>` to include the README in the package:

```xml
<ItemGroup>
  <None Include="README.md" Pack="true" PackagePath="/" />
</ItemGroup>
```

**Package IDs:** `Winix.TimeIt`, `Winix.Squeeze`, `Winix.Peep` (NuGet CLI is case-insensitive, so `dotnet tool install -g winix.timeit` works).

## 3. Release Workflow

New file: `.github/workflows/release.yml`

### Triggers

- **Tag push** matching `v*` (e.g. `v0.1.0`)
- **Manual dispatch** (`workflow_dispatch`) with a `version` input string

### Version resolution

- Tag trigger: strip the `v` prefix from the tag name (e.g. `v0.1.0` → `0.1.0`)
- Manual trigger: use the `version` input directly
- Passed to dotnet as `/p:Version=$VERSION` to override `Directory.Build.props`

### Jobs

#### Job 1: `build-and-test`

Same 3-platform matrix as `ci.yml` (ubuntu, windows, macos). Runs restore, build, test. Gates all subsequent jobs.

#### Job 2: `pack-nuget`

- Depends on: `build-and-test`
- Runs on: `ubuntu-latest`
- Steps:
  1. Checkout + setup .NET
  2. `dotnet pack` each tool project with `-c Release /p:Version=$VERSION`
  3. Upload `.nupkg` files as artifact `nuget-packages`

#### Job 3: `publish-aot`

- Depends on: `build-and-test`
- Matrix: `win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`
- Runs on: appropriate OS for each RID (windows-latest for win-x64, ubuntu-latest for linux-x64, macos-latest for osx-*)
- Steps:
  1. Checkout + setup .NET
  2. `dotnet publish` each tool project with `-c Release -r $RID /p:Version=$VERSION`
  3. Zip each tool's publish output (e.g. `timeit-win-x64.zip`)
  4. Upload zips as artifact `aot-$RID`

#### Job 4: `release`

- Depends on: `pack-nuget`, `publish-aot`
- Runs on: `ubuntu-latest`
- Steps:
  1. Download all artifacts (nuget-packages, aot-*)
  2. Create GitHub Release using `gh release create` with the version tag
  3. Attach all `.nupkg` and `.zip` files to the release
  4. Publish to NuGet.org: `dotnet nuget push *.nupkg --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json`

### Required secret

`NUGET_API_KEY` — NuGet.org API key with push permissions for the Winix.* packages. Must be added manually to GitHub repo Settings → Secrets → Actions.

## 4. AOT Binary Naming

Each tool produces a zip named: `{tool}-{rid}.zip`

Examples:
- `timeit-win-x64.zip`
- `squeeze-linux-x64.zip`
- `peep-osx-arm64.zip`

Contents: the single native binary (e.g. `timeit.exe` on Windows, `timeit` on Linux/macOS).

## File Map

| File | Action |
|------|--------|
| `LICENSE` | Create — MIT license |
| `Directory.Build.props` | Modify — add NuGet shared metadata |
| `src/timeit/timeit.csproj` | Modify — add PackageId, Description, PackageReadmeFile |
| `src/squeeze/squeeze.csproj` | Modify — add PackageId, Description, PackageReadmeFile |
| `src/peep/peep.csproj` | Modify — add PackageId, Description, PackageReadmeFile |
| `.github/workflows/release.yml` | Create — release workflow |
| `CLAUDE.md` | Modify — add release/versioning conventions |

## Manual Steps After Implementation

1. **Add NuGet API key:** GitHub repo → Settings → Secrets → Actions → New secret → Name: `NUGET_API_KEY`, Value: your NuGet.org API key
2. **Test the workflow:** Push a tag `v0.1.0` or use manual dispatch to verify the pipeline end-to-end
3. **Reserve package names:** Consider pushing 0.1.0 early to claim `Winix.TimeIt`, `Winix.Squeeze`, `Winix.Peep` on NuGet.org
