# Winix

Cross-platform CLI tool suite (Windows + *nix). .NET / C# / AOT.

> **Are you an AI agent?** This file is for agents working *on* Winix (modifying source, adding tools, running tests). If you are an agent *using* Winix tools on a user's machine for some other task, read [`AGENTS.md`](AGENTS.md) instead — it sets out when a Winix tool is actually the right choice versus a POSIX or Windows default. See also [`llms.txt`](llms.txt) for the structured tool catalogue and [`docs/ai/`](docs/ai/) for per-tool agent guides.

## Build

```
dotnet build Winix.sln
```

## Test

```
dotnet test Winix.sln
```

## Publish (AOT native binary)

```
dotnet publish src/timeit/timeit.csproj -c Release -r win-x64
```

## Architecture

- **Class libraries** (`Winix.TimeIt`, etc.) contain all logic — testable without process spawning for formatting, integration tests for process execution
- **Console apps** (`timeit`, etc.) are thin entry points — arg parsing, call library, set exit code
- **Shared libraries** — referenced across tools:
  - `Yort.ShellKit` — console env, ANSI colour, `CommandLineParser` (mandatory for arg parsing, see Conventions), duration/exit-code helpers, JSON helper, gitignore filter, `GlobMatcher` (regex-based glob predicate; lives here so the lowest layer owns it — `FileWalk` and `TreeX` consume it)
  - `Winix.FileWalk` — directory walking, gitignore, size/content predicates (glob matching via ShellKit's `GlobMatcher`)
  - `Winix.Codec` — Crockford base32, secure RNG, hex, base64, constant-time compare
  - `Winix.QrCode` — QR encoder + 4 renderers (unicode/ascii/svg/png); internal only
  - `Winix.SecretStore` — DPAPI / Keychain / libsecret abstraction; enumeration via native APIs on Windows/Linux and self-healing index on macOS

## Conventions

- TDD: write failing test, implement, verify, commit
- AOT-compatible: no unconstrained reflection, use trim analyzers
- All CLI arg parsing MUST use `Yort.ShellKit.CommandLineParser`. Hand-rolling is treated as a defect — use the shared parser so `--describe`, `--help`, error formatting, and exit-code conventions stay consistent across the suite. Subcommand tools dispatch on `positional[0]` (see `schedule`, `url`, `qr` for precedent).
- Always use `ProcessStartInfo.ArgumentList` for passing arguments to child processes — never build argument strings via interpolation or concatenation. String-based `Arguments` is prone to quoting/escaping bugs (especially trailing-backslash injection on Windows). If a framework or OS bug forces using the string `Arguments` property, document the reason in the code with a comment explaining why `ArgumentList` cannot be used.
- All output formatting in class library (testable), all I/O in console app
- Summary output goes to stderr by default (don't pollute piped command output)
- Respect `NO_COLOR` env var (no-color.org)
- Exit codes: pass through child process exit code; 125/126/127 for tool's own errors (POSIX convention)
- Full braces always, nullable reference types enabled, warnings as errors
- Platform-gated integration tests (e.g. `IntegrationTests_Windows.cs`, `IntegrationTests_Linux.cs`) MUST use `SkippableFact` + `Skip.IfNot(OperatingSystem.IsX(), "reason")` rather than `if (!OperatingSystem.IsX()) return;`. The early-return pattern reports the test as Passed on the wrong platform — a CI false positive. Xunit.SkippableFact package (already added to envvault tests) emits a proper Skipped status instead. Keep a redundant `if (!IsX()) return;` after the `Skip.IfNot` to satisfy the CA1416 analyzer — comment that this is deliberate.
- Console apps use proper `namespace`/`class Program`/`static Main` — no top-level statements
- Console apps are thin: arg parsing, validation, constructing library objects, error output. Stream orchestration, event loops, and domain logic belong in the class library.
- Each tool has a `README.md` in its console app directory (e.g. `src/timeit/README.md`). Keep these up to date when flags, options, or behaviour change. Follow the existing pattern: description, install, usage/examples, options table, exit codes, colour section.
- Versioning: `Directory.Build.props` holds the dev version. Release builds override via `/p:Version=X.Y.Z`. Tag format: `v0.1.0`.
- To release: push a tag `vX.Y.Z` to main, or use manual workflow dispatch in GitHub Actions.
- NuGet package IDs: `Winix.TimeIt`, `Winix.Squeeze`, `Winix.Peep`, `Winix.Wargs`, `Winix.Files`, `Winix.TreeX`, `Winix.Man`, `Winix.Less`, `Winix.WhoHolds`, `Winix.Schedule`, `Winix.NetCat`, `Winix.Winix`, `Winix.Retry`, `Winix.When`, `Winix.Clip`, `Winix.Ids`, `Winix.Digest`, `Winix.Notify`, `Winix.Url`, `Winix.Qr`, `Winix.Protect`, `Winix.Unprotect`, `Winix.EnvVault`, `Winix.MkSecret`, `Winix.Trash`, `Winix.HCat`, `Winix.Demux`. Publishing requires `NUGET_API_KEY` secret in GitHub repo settings. NuGet packages are .NET global tools (JIT, framework-dependent via `PackAsTool`). GitHub releases and Scoop deliver AOT native binaries. This dual-mode distribution is intentional.
- Scoop bucket: `bucket/` directory contains scoop manifests (`timeit.json`, `squeeze.json`, `peep.json`, `wargs.json`, `files.json`, `treex.json`, `man.json`, `less.json`, `whoholds.json`, `schedule.json`, `nc.json`, `winix.json`, `retry.json`, `when.json`, `clip.json`, `ids.json`, `digest.json`, `notify.json`, `url.json`, `qr.json`, `protect.json`, `unprotect.json`, `envvault.json`, `mksecret.json`, `trash.json`, `hcat.json`, `demux.json`). Updated automatically by the release pipeline.
- Winget manifests: generated by the release pipeline for stable versions only (no `-` in version string). Uploaded as `winget-manifests` artifact. Submitted manually to `microsoft/winget-pkgs`.
- When adding a new tool:
  - Arg parsing: build on `Yort.ShellKit.CommandLineParser` (never hand-roll). For multi-subcommand tools, dispatch on `positional[0]` after a top-level parse (precedent: `schedule`, `url`, `qr`).
  - Create `bucket/{tool}.json` scoop manifest. (Do NOT edit `bucket/winix.json` — it is the suite-installer stub, a single-binary manifest that runs `winix install --via scoop` as its post-install and delegates to per-tool manifests.)
  - Add the tool to `.github/workflows/release.yml`: `dotnet publish` step per `matrix.rid`, `dotnet pack` step (for NuGet), per-tool zip steps (Linux/macOS + Windows), combined zip `Copy-Item`, and the tool map (`tools: { … }`) entry.
  - Add the tool to `.github/workflows/post-publish.yml`: `update_manifest bucket/{tool}.json …` line and `generate_manifests "{tool}" "{Tool}" "…" "tag1,tag2,tag3"` line. The 4th argument is a comma-separated list of winget tags (3-5 domain-specific — the shared baseline `cli,developer-tools,portable,winix` is added automatically). Winget allows up to 16 tags per package; keep domain tags aligned with the nuget `<PackageTags>` in the csproj.
  - Create `src/{tool}/README.md` with install sections, `src/{tool}/man/man1/{tool}.1` groff page, and reference the man page in `src/{tool}/{tool}.csproj` via `<Content Include="man\man1\{tool}.1" CopyToPublishDirectory="PreserveNewest" Link="share\man\man1\{tool}.1" />`.
  - Set `<Description>` in `src/{tool}/{tool}.csproj` to a distinct one-line summary (first thing people see on nuget.org and in `winget show`).
  - Set `<PackageTags>` in `src/{tool}/{tool}.csproj` using the shared baseline `cli;command-line;cross-platform;windows;macos;linux;aot;dotnet-tool;winix` plus 3-5 domain-specific tags (e.g. `hmac;sha256;sha512;blake2;crypto;checksum` on digest, `cron;scheduler;task-scheduler;crontab;rrule` on schedule). Tags drive nuget.org filtered search — without them the package is only found via exact-name lookup.
  - Create `docs/ai/{tool}.md` AI agent guide and add the tool to `llms.txt`.
  - Test csproj MUST mirror the app csproj's `<UseSystemResourceKeys>true</UseSystemResourceKeys>` (NOT just `InvariantGlobalization`). Under `UseSystemResourceKeys`, framework exception `.Message` returns bare SR resource keys instead of English; only a test csproj that also sets this flag reproduces the leak. Without it, resource-key regression tests pass spuriously (the JIT host resolves English).
  - Never pipe a framework exception's `.Message` to user output. Use `Yort.ShellKit.SafeError.Describe(ex)` (type-maps common CoreLib exceptions to English, falls back to `ex.GetType().Name`). Adding `ex.GetType().Name` alongside the message is the acceptable minimum for broad catches. Exception: leave bespoke text that is genuinely better (a project exception with a literal English message, native-OS text from `Win32Exception`/`SocketException`).
  - Create a native capability `run-smokes.sh` fixture (derive cases from the tool's README options/exit-code surface) and add the tool to `.github/workflows/manual-smoke.yml` (tool list + `runner_for` map + sed retarget rule).
  - Update `CLAUDE.md`: project layout, NuGet package IDs list, scoop manifests list.
  - **CHANGELOG.md (only when the tool actually ships in a stable release, not pre-releases).** Create `src/{tool}/CHANGELOG.md` in Keep-a-Changelog format (see `src/timeit/CHANGELOG.md` for the template). First published version gets a single `- Initial release.` entry; subsequent versions get real Added/Changed/Fixed/Removed sections. Pre-release batches (e.g. v0.3.x, v0.4.x before tagging) don't need a CHANGELOG — add one at the point of first stable tag. The shared `Directory.Build.targets` automatically extracts the latest `## [version]` section into `<PackageReleaseNotes>` at pack time and appends a "See full changelog" link to the GitHub copy, so consumers installing via nuget.org / winget / `dotnet add package` see the release notes without any per-csproj wiring.

## Windows Defender false positive

The `Winix.Squeeze.Tests` project may trigger Windows Defender (`Trojan:MSIL/Formbook.NE!MTB`). This is a false positive caused by ZstdSharp.Port's compression byte patterns triggering ML-based heuristic detection. The library has 195M+ NuGet downloads and a clean ReversingLabs scan.

To resolve, add a Defender exclusion for the repo directory (elevated PowerShell):
```powershell
Add-MpPreference -ExclusionPath 'd:\projects\winix'
```

## Project layout

```
bucket/                    — scoop manifests (updated by release pipeline)
src/Yort.ShellKit/         — shared library (ConsoleEnv, AnsiColor, CommandLineParser, DurationParser, ExitCode, GitIgnoreFilter, JsonHelper, ParseResult, SafeRegex, DisplayFormat)
src/Winix.TimeIt/          — class library (timing logic, formatting)
src/timeit/                — console app entry point
src/Winix.Squeeze/         — class library (compression, format detection, formatting)
src/squeeze/               — console app entry point
src/Winix.Peep/            — class library (command execution, scheduling, file watching, rendering)
src/peep/                  — console app entry point
src/Winix.Wargs/           — class library (input reading, command builder, job execution, formatting)
src/wargs/                 — console app entry point
src/Winix.FileWalk/        — shared library (directory walking, predicates, glob/regex matching)
src/Winix.Codec/           — shared library (Crockford base32, secure RNG, hex, base64, constant-time compare)
src/Winix.Files/           — class library (output formatting)
src/files/                 — console app entry point
src/Winix.TreeX/           — class library (tree building, rendering)
src/treex/                 — console app entry point
src/Winix.Man/             — class library (parser, renderer, discovery, pager)
src/man/                   — console app entry point
src/Winix.Less/            — class library (input, screen, search, follow, pager)
src/less/                  — console app entry point
src/Winix.WhoHolds/        — class library (finders, formatting, argument parsing)
src/whoholds/              — console app entry point
src/Winix.Schedule/        — class library (cron parser, schtasks/crontab backends, formatting)
src/schedule/              — console app entry point
src/Winix.Retry/           — class library (retry loop, backoff, formatting)
src/retry/                 — console app entry point
src/Winix.When/            — class library (parsing, conversion, formatting)
src/when/                  — console app entry point (InvariantGlobalization=false for ICU)
src/Winix.Clip/            — class library (backends, arg parsing, formatting)
src/clip/                  — console app entry point
src/Winix.Ids/             — class library (generators, arg parser, formatting)
src/ids/                   — console app entry point
src/Winix.Digest/          — class library (hashers, HMAC, key resolver, formatting)
src/digest/                — console app entry point
src/Winix.Notify/          — class library (backends, dispatcher, arg parser, formatting)
src/notify/                — console app entry point
src/Winix.Url/             — class library (encoder, decoder, parser, builder, joiner, query editor, formatting, arg parser)
src/url/                   — console app entry point
src/Winix.QrCode/          — shared library (QR encoder + 4 renderers: unicode, ascii, svg, png)
src/Winix.Qr/              — class library (CLI shape, helper payload builders, arg parser)
src/qr/                    — console app entry point
src/Winix.SecretStore/     — shared library (ISecretStore abstraction; Cred Manager / Keychain / libsecret backends)
src/Winix.Protect/         — class library (backends, chunk stream orchestration, in-place executor)
src/protect/               — console app entry point (PackageId Winix.Protect)
src/unprotect/             — console app entry point (PackageId Winix.Unprotect, same library)
src/Winix.EnvVault/        — class library (ArgParser, Cli, ExecRunner, ValuePrompt, Formatting)
src/envvault/              — console app entry point (PackageId Winix.EnvVault)
src/Winix.MkSecret/        — class library (generators, sampling, formatting, arg parsing)
src/mksecret/              — console app entry point
src/Winix.Trash/           — class library (recycle-bin backends, list/empty orchestration, formatting, arg parsing)
src/trash/                 — console app entry point
src/Winix.HCat/            — class library (Kestrel host, serve/inspect/pipe handlers, bind/safety, CI stop conditions, self-signed cert, arg parsing)
src/hcat/                  — console app entry point (PackageId Winix.HCat)
src/Winix.Demux/           — class library (router, sinks, summary, arg parsing, Cli.Run seam)
src/demux/                 — console app entry point
src/Winix.Winix/           — class library (PM adapters, manifest, orchestration)
src/winix/                 — console app entry point (suite installer)
tests/Yort.ShellKit.Tests/ — xUnit tests for shared library
tests/Winix.TimeIt.Tests/  — xUnit tests
tests/Winix.Squeeze.Tests/ — xUnit tests
tests/Winix.Peep.Tests/    — xUnit tests
tests/Winix.Wargs.Tests/   — xUnit tests
tests/Winix.FileWalk.Tests/ — xUnit tests
tests/Winix.Files.Tests/   — xUnit tests
tests/Winix.TreeX.Tests/   — xUnit tests
tests/Winix.Man.Tests/     — xUnit tests
tests/Winix.Less.Tests/    — xUnit tests
tests/Winix.WhoHolds.Tests/ — xUnit tests
tests/Winix.Schedule.Tests/ — xUnit tests
tests/Winix.Retry.Tests/   — xUnit tests
tests/Winix.When.Tests/    — xUnit tests
tests/Winix.Clip.Tests/    — xUnit tests
tests/Winix.Codec.Tests/   — xUnit tests
tests/Winix.Ids.Tests/     — xUnit tests
tests/Winix.Digest.Tests/  — xUnit tests
tests/Winix.Notify.Tests/  — xUnit tests
tests/Winix.Url.Tests/     — xUnit tests
tests/Winix.QrCode.Tests/  — xUnit tests
tests/Winix.Qr.Tests/      — xUnit tests
tests/Winix.SecretStore.Tests/ — xUnit tests
tests/Winix.Protect.Tests/     — xUnit tests
tests/Winix.EnvVault.Tests/    — xUnit tests
tests/Winix.MkSecret.Tests/ — xUnit tests
tests/Winix.Trash.Tests/   — xUnit tests
tests/Winix.HCat.Tests/    — xUnit tests
tests/Winix.Demux.Tests/   — xUnit tests
tests/Winix.Winix.Tests/   — xUnit tests
```
