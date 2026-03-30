# Release Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add release infrastructure to ship Winix tools as NuGet packages and AOT native binaries via GitHub Releases.

**Architecture:** NuGet metadata in Directory.Build.props (shared) and per-tool csproj files. A GitHub Actions release workflow triggered by version tags or manual dispatch builds, packs, publishes AOT binaries, creates a GitHub Release, and pushes to NuGet.org.

**Tech Stack:** GitHub Actions, .NET 10, NuGet, AOT publishing

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `LICENSE` | Create | MIT license text |
| `Directory.Build.props` | Modify | Add shared NuGet metadata |
| `src/timeit/timeit.csproj` | Modify | Add PackageId, Description, PackageReadmeFile |
| `src/squeeze/squeeze.csproj` | Modify | Add PackageId, Description, PackageReadmeFile |
| `src/peep/peep.csproj` | Modify | Add PackageId, Description, PackageReadmeFile |
| `.github/workflows/release.yml` | Create | Release workflow (build, test, pack, AOT publish, release) |
| `CLAUDE.md` | Modify | Add release/versioning conventions |

---

### Task 1: Add MIT license file

**Files:**
- Create: `LICENSE`

- [ ] **Step 1: Create LICENSE file**

Create `LICENSE` at the repo root with standard MIT license text:

```
MIT License

Copyright (c) Troy Willmot

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

- [ ] **Step 2: Commit**

```
git add LICENSE
git commit -m "chore: add MIT license"
```

---

### Task 2: Add NuGet metadata to Directory.Build.props and tool csproj files

**Files:**
- Modify: `Directory.Build.props`
- Modify: `src/timeit/timeit.csproj`
- Modify: `src/squeeze/squeeze.csproj`
- Modify: `src/peep/peep.csproj`

- [ ] **Step 1: Add shared metadata to Directory.Build.props**

The current file has a single `<PropertyGroup>`. Add the NuGet properties to it. The full file should be:

```xml
<Project>
  <PropertyGroup>
    <Version>0.1.0</Version>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
    <Authors>Troy Willmot</Authors>
    <Copyright>Copyright (c) Troy Willmot</Copyright>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <RepositoryUrl>https://github.com/Yortw/winix</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageProjectUrl>https://github.com/Yortw/winix</PackageProjectUrl>
    <PackageTags>cli;tools;cross-platform;aot</PackageTags>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Add per-tool metadata to timeit.csproj**

The current `timeit.csproj` has `<PackAsTool>true</PackAsTool>` and `<ToolCommandName>timeit</ToolCommandName>` but no PackageId or Description. Add them to the existing `<PropertyGroup>`, and add an `<ItemGroup>` for the README. The full file should be:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
    <OptimizationPreference>Size</OptimizationPreference>
    <StackTraceSupport>false</StackTraceSupport>
    <UseSystemResourceKeys>true</UseSystemResourceKeys>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>timeit</ToolCommandName>
    <PackageId>Winix.TimeIt</PackageId>
    <Description>Time a command — wall clock, CPU time, peak memory, exit code. A cross-platform 'time' replacement.</Description>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Winix.TimeIt\Winix.TimeIt.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="/" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Add per-tool metadata to squeeze.csproj**

Same pattern. Full file:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
    <OptimizationPreference>Size</OptimizationPreference>
    <StackTraceSupport>false</StackTraceSupport>
    <UseSystemResourceKeys>true</UseSystemResourceKeys>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>squeeze</ToolCommandName>
    <PackageId>Winix.Squeeze</PackageId>
    <Description>Compress and decompress files using gzip, brotli, or zstd. A cross-platform multi-format compression tool.</Description>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Winix.Squeeze\Winix.Squeeze.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="/" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Add per-tool metadata to peep.csproj**

Same pattern. Full file:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
    <OptimizationPreference>Size</OptimizationPreference>
    <StackTraceSupport>false</StackTraceSupport>
    <UseSystemResourceKeys>true</UseSystemResourceKeys>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>peep</ToolCommandName>
    <PackageId>Winix.Peep</PackageId>
    <Description>Run a command repeatedly and display output on a refreshing screen. A cross-platform 'watch' + 'entr' replacement.</Description>
    <PackageReadmeFile>README.md</PackageReadmeFile>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Winix.Peep\Winix.Peep.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="/" />
  </ItemGroup>
</Project>
```

- [ ] **Step 5: Verify pack works locally**

Run: `dotnet pack src/timeit/timeit.csproj -c Release -o tmp_pack`
Run: `dotnet pack src/squeeze/squeeze.csproj -c Release -o tmp_pack`
Run: `dotnet pack src/peep/peep.csproj -c Release -o tmp_pack`
Expected: All three produce `.nupkg` files in `tmp_pack/` with 0 warnings.

Then verify the package contents include the README:

Run: `unzip -l tmp_pack/Winix.TimeIt.0.1.0.nupkg | grep -i readme`
Expected: Shows `README.md` in the package root.

Clean up: `rm -rf tmp_pack`

- [ ] **Step 6: Commit**

```
git add Directory.Build.props src/timeit/timeit.csproj src/squeeze/squeeze.csproj src/peep/peep.csproj
git commit -m "feat: add NuGet package metadata for all tools"
```

---

### Task 3: Create release workflow

**Files:**
- Create: `.github/workflows/release.yml`

- [ ] **Step 1: Create the release workflow**

Create `.github/workflows/release.yml`:

```yaml
name: Release

on:
  push:
    tags: ['v*']
  workflow_dispatch:
    inputs:
      version:
        description: 'Version to release (e.g. 0.1.0)'
        required: true
        type: string

permissions:
  contents: write

env:
  DOTNET_VERSION: '10.0.x'

jobs:
  resolve-version:
    runs-on: ubuntu-latest
    outputs:
      version: ${{ steps.version.outputs.version }}
    steps:
      - name: Resolve version
        id: version
        run: |
          if [ "${{ github.event_name }}" = "workflow_dispatch" ]; then
            echo "version=${{ inputs.version }}" >> "$GITHUB_OUTPUT"
          else
            TAG="${{ github.ref_name }}"
            echo "version=${TAG#v}" >> "$GITHUB_OUTPUT"
          fi

  build-and-test:
    needs: resolve-version
    strategy:
      fail-fast: false
      matrix:
        os: [ubuntu-latest, windows-latest, macos-latest]
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      - name: Restore
        run: dotnet restore Winix.sln

      - name: Build
        run: dotnet build Winix.sln --no-restore -c Release

      - name: Test
        run: dotnet test Winix.sln --no-build -c Release --logger "trx;LogFileName=results.trx"

      - name: Upload test results
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: test-results-${{ matrix.os }}
          path: '**/TestResults/*.trx'
          retention-days: 7

  pack-nuget:
    needs: [resolve-version, build-and-test]
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      - name: Pack timeit
        run: dotnet pack src/timeit/timeit.csproj -c Release -o packages /p:Version=${{ needs.resolve-version.outputs.version }}

      - name: Pack squeeze
        run: dotnet pack src/squeeze/squeeze.csproj -c Release -o packages /p:Version=${{ needs.resolve-version.outputs.version }}

      - name: Pack peep
        run: dotnet pack src/peep/peep.csproj -c Release -o packages /p:Version=${{ needs.resolve-version.outputs.version }}

      - name: Upload NuGet packages
        uses: actions/upload-artifact@v4
        with:
          name: nuget-packages
          path: packages/*.nupkg
          retention-days: 7

  publish-aot:
    needs: [resolve-version, build-and-test]
    strategy:
      fail-fast: false
      matrix:
        include:
          - rid: win-x64
            os: windows-latest
          - rid: linux-x64
            os: ubuntu-latest
          - rid: osx-x64
            os: macos-latest
          - rid: osx-arm64
            os: macos-latest
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      - name: Publish timeit
        run: dotnet publish src/timeit/timeit.csproj -c Release -r ${{ matrix.rid }} /p:Version=${{ needs.resolve-version.outputs.version }}

      - name: Publish squeeze
        run: dotnet publish src/squeeze/squeeze.csproj -c Release -r ${{ matrix.rid }} /p:Version=${{ needs.resolve-version.outputs.version }}

      - name: Publish peep
        run: dotnet publish src/peep/peep.csproj -c Release -r ${{ matrix.rid }} /p:Version=${{ needs.resolve-version.outputs.version }}

      - name: Zip binaries (Linux/macOS)
        if: runner.os != 'Windows'
        run: |
          cd src/timeit/bin/Release/net10.0/${{ matrix.rid }}/publish && zip -j $GITHUB_WORKSPACE/timeit-${{ matrix.rid }}.zip * && cd $GITHUB_WORKSPACE
          cd src/squeeze/bin/Release/net10.0/${{ matrix.rid }}/publish && zip -j $GITHUB_WORKSPACE/squeeze-${{ matrix.rid }}.zip * && cd $GITHUB_WORKSPACE
          cd src/peep/bin/Release/net10.0/${{ matrix.rid }}/publish && zip -j $GITHUB_WORKSPACE/peep-${{ matrix.rid }}.zip * && cd $GITHUB_WORKSPACE

      - name: Zip binaries (Windows)
        if: runner.os == 'Windows'
        shell: pwsh
        run: |
          Compress-Archive -Path src/timeit/bin/Release/net10.0/${{ matrix.rid }}/publish/* -DestinationPath timeit-${{ matrix.rid }}.zip
          Compress-Archive -Path src/squeeze/bin/Release/net10.0/${{ matrix.rid }}/publish/* -DestinationPath squeeze-${{ matrix.rid }}.zip
          Compress-Archive -Path src/peep/bin/Release/net10.0/${{ matrix.rid }}/publish/* -DestinationPath peep-${{ matrix.rid }}.zip

      - name: Upload AOT binaries
        uses: actions/upload-artifact@v4
        with:
          name: aot-${{ matrix.rid }}
          path: '*.zip'
          retention-days: 7

  release:
    needs: [resolve-version, pack-nuget, publish-aot]
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      - name: Download NuGet packages
        uses: actions/download-artifact@v4
        with:
          name: nuget-packages
          path: packages

      - name: Download AOT binaries (win-x64)
        uses: actions/download-artifact@v4
        with:
          name: aot-win-x64
          path: aot

      - name: Download AOT binaries (linux-x64)
        uses: actions/download-artifact@v4
        with:
          name: aot-linux-x64
          path: aot

      - name: Download AOT binaries (osx-x64)
        uses: actions/download-artifact@v4
        with:
          name: aot-osx-x64
          path: aot

      - name: Download AOT binaries (osx-arm64)
        uses: actions/download-artifact@v4
        with:
          name: aot-osx-arm64
          path: aot

      - name: Create GitHub Release
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          VERSION="${{ needs.resolve-version.outputs.version }}"
          TAG="v${VERSION}"

          # Create tag if manual dispatch (tag already exists for tag-push trigger)
          if [ "${{ github.event_name }}" = "workflow_dispatch" ]; then
            git tag "$TAG"
            git push origin "$TAG"
          fi

          gh release create "$TAG" \
            --title "v${VERSION}" \
            --generate-notes \
            packages/*.nupkg \
            aot/*.zip

      - name: Publish to NuGet.org
        env:
          NUGET_API_KEY: ${{ secrets.NUGET_API_KEY }}
        run: |
          for pkg in packages/*.nupkg; do
            dotnet nuget push "$pkg" --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json --skip-duplicate
          done
```

- [ ] **Step 2: Validate workflow syntax**

Run: `python -c "import yaml; yaml.safe_load(open('.github/workflows/release.yml'))" 2>&1 || echo "YAML parse error"`

If python/pyyaml not available, at minimum check it's valid YAML:

Run: `head -5 .github/workflows/release.yml`
Expected: Shows the `name: Release` header.

- [ ] **Step 3: Commit**

```
git add .github/workflows/release.yml
git commit -m "feat: add release workflow for NuGet and AOT binaries"
```

---

### Task 4: Update CLAUDE.md with release conventions

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Add release conventions**

After the existing `## Conventions` section's last bullet, add:

```markdown
- Versioning: `Directory.Build.props` holds the dev version. Release builds override via `/p:Version=X.Y.Z`. Tag format: `v0.1.0`.
- To release: push a tag `vX.Y.Z` to main, or use manual workflow dispatch in GitHub Actions.
- NuGet package IDs: `Winix.TimeIt`, `Winix.Squeeze`, `Winix.Peep`. Publishing requires `NUGET_API_KEY` secret in GitHub repo settings.
```

- [ ] **Step 2: Commit**

```
git add CLAUDE.md
git commit -m "docs: add release conventions to CLAUDE.md"
```

---

### Task 5: Verify full build and pack

**Files:** None (verification only)

- [ ] **Step 1: Full build**

Run: `dotnet build Winix.sln`
Expected: Build succeeded, 0 errors, 0 warnings

- [ ] **Step 2: Full test suite**

Run: `dotnet test Winix.sln`
Expected: All tests pass.

- [ ] **Step 3: Pack all three tools**

Run: `dotnet pack src/timeit/timeit.csproj -c Release -o tmp_pack /p:Version=0.1.0`
Run: `dotnet pack src/squeeze/squeeze.csproj -c Release -o tmp_pack /p:Version=0.1.0`
Run: `dotnet pack src/peep/peep.csproj -c Release -o tmp_pack /p:Version=0.1.0`
Expected: All three produce `.nupkg` files with 0 warnings.

Verify package contents:

Run: `ls tmp_pack/`
Expected: `Winix.TimeIt.0.1.0.nupkg`, `Winix.Squeeze.0.1.0.nupkg`, `Winix.Peep.0.1.0.nupkg`

Clean up: `rm -rf tmp_pack`

- [ ] **Step 4: Commit any fixups**

If any fixes were needed during verification:
```
git add -A
git commit -m "fix: address packaging issues"
```
