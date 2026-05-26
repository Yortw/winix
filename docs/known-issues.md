# Known Issues

Issues identified during pre-release code review that are deferred to a future version. None are release blockers.

## treex: Symlink directories misclassified in tree pipeline

Symlink directories get `Type = Symlink` but multiple code paths check `Type == Directory` for directory-specific behaviour. This affects rendering (symlink dir children not shown), pruning (symlink dirs treated as leaves), size rollup (symlink dirs excluded), sorting (symlink dirs sort with files), and NDJSON counting (symlink dirs counted as files).

Only manifests when the directory tree contains symbolic links pointing to directories, which is uncommon in typical project trees. The output is silently incomplete rather than incorrect — the symlink entry itself is shown, but its children are not expanded.

**Fix:** Add an `IsDirectoryLike` helper or change symlink directories to retain `Type = Directory` with a separate `IsSymlink` flag.

## files: gitignore filtering spawns one process per path

`GitIgnoreFilter.IsIgnored` spawns a `git check-ignore -q` process for every file and directory encountered during a walk. On a repo with thousands of files this is very slow. The `CheckBatch` method exists for efficient batch checking but `FileWalker` calls the per-path `IsIgnored` via its `Func<string, bool>` predicate interface.

Correct results, just slow. The architecture would need a look-ahead buffer or an in-process gitignore parser for proper streaming batch support.

## CI: No linux-arm64 native binary

The release pipeline builds AOT binaries for `win-x64`, `linux-x64`, `osx-x64`, and `osx-arm64` but not `linux-arm64`. Linux ARM64 users can install via NuGet global tool (framework-dependent, architecture-neutral).

Waiting on GitHub Actions `linux-arm64` runners reaching GA in the free tier.

## CI: Combined zip is Windows-only

The combined `winix-win-x64.zip` (all tools in one download) is only built for Windows (for the Scoop `winix` manifest). Other platforms get per-tool zip files. This is a conscious design choice — add combined zips for other platforms if there's demand.

## Release artifacts: debug symbols ship separately

As of v0.4.0, each GitHub release `<tool>-<rid>.zip` contains the binary and man pages only. Debug symbols (`.pdb` on all platforms, plus the native `.dbg` on Linux) are published in a companion `<tool>-<rid>-symbols.zip`. This keeps the default download small — native symbols were 64–85% of each pre-v0.4.0 zip — while preserving symbolication for crash/dump analysis.

- **`-symbols.zip` assets are checksummed but not Authenticode-signed.** Only the executable-bearing `*-win-x64.zip` tool zips are signed; symbols zips are debug-only and never executed. They are listed in `SHA256SUMS` for integrity.
- **macOS `-symbols.zip` contains managed `.pdb`s only.** NativeAOT produces no separate native symbol file on macOS, so these zips do not enable native symbolication of the shipped binary; they are emitted for cross-platform uniformity.

## Windows Defender false positives on AOT binaries

Windows Defender may flag one or more Winix native binaries as malicious. These are false positives. Known detections include:

- `Trojan:MSIL/Formbook.NE!MTb` on `Winix.Squeeze.Tests` — triggered by byte patterns in the ZstdSharp.Port compression library (195M+ NuGet downloads, clean ReversingLabs scan).
- `Trojan:Win32/Sprisky.U!cl` on `timeit.exe` — a cloud-based ML heuristic detection (`!cl`). The timeit tool uses Win32 APIs (`GetProcessTimes`, `GetProcessMemoryInfo`) to measure child process performance, which is behaviourally similar to process-monitoring malware from the perspective of ML-based scanners.

These detections are heuristic, not signature-based — the scanner's ML model considers the binary's structural features suspicious, not because it matches known malware bytes. Small, unsigned native binaries that spawn and inspect processes are a common false-positive pattern.

**If you encounter a detection:** add a Windows Defender exclusion for the binary or the Winix install directory, or submit the file to [Microsoft Security Intelligence](https://www.microsoft.com/en-us/wdsi/filesubmission) as a false positive. We submit binaries for review with each release but new detections can appear as Microsoft updates their ML models.

**Code signing.** Windows x64 binaries *are* Authenticode-signed (Certum certificate, RFC 3161 timestamped) as part of the release process: the draft release is signed locally before publishing via `scripts/sign-release.sh`. Signing reduces these heuristic false positives but does not eliminate ML-based ones, so a detection can still occasionally surface. Linux/macOS binaries and the `-symbols.zip` assets are not code-signed (Authenticode is Windows-only, and macOS notarization is not currently part of the pipeline).

## CI: GitHub Actions not pinned to commit SHAs

Third-party actions (`actions/checkout@v6`, `actions/setup-dotnet@v5`, etc.) use version tags rather than pinned commit SHAs. This is standard practice for open-source projects and Dependabot monitors for updates, but pinned SHAs would provide stronger supply-chain protection. Additionally, `actions/upload-artifact@v7` and `actions/download-artifact@v8` are on different major versions — functionally compatible but worth aligning when upload v8 ships.
