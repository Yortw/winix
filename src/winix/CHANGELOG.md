# Changelog

All notable changes to **winix** (the suite installer) will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Changed
- `winix list`, `winix status`, and `winix uninstall` now use a single bulk subprocess per package manager (`winget list`, `scoop list`, `brew list --versions`, `dotnet tool list -g`) instead of one filtered subprocess per tool. On real winget this drops the wall-time from 5-7 minutes to ~8 seconds for a 22-tool manifest. The previous implementation issued up to 88 filtered subprocess calls (22 tools × 2 PMs × `IsInstalled` + `GetInstalledVersion`); each `winget list --id X --exact` was measured at 7-19 seconds.
- `winix list` and `winix uninstall` now emit a `winix: querying <pm>…` progress line on stderr immediately before each package manager's bulk query. Suppressed under `--json`.

## [0.2.0] - 2026-04-16

- Initial release.
