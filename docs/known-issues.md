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

## CI: GitHub Actions not pinned to commit SHAs

Third-party actions (`actions/checkout@v6`, `actions/setup-dotnet@v5`, etc.) use version tags rather than pinned commit SHAs. This is standard practice for open-source projects and Dependabot monitors for updates, but pinned SHAs would provide stronger supply-chain protection. Additionally, `actions/upload-artifact@v7` and `actions/download-artifact@v8` are on different major versions — functionally compatible but worth aligning when upload v8 ships.
