# Changelog

All notable changes to **peep** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/).

## [0.3.0] - 2026-05-10

### Fixed
- `peep <command>` no longer crashes with an SR resource-key exception (`InvalidOperationException: Cannot read keys when …`) when stdin is redirected (e.g. `peep ls < /dev/null`, or any pipeline that doesn't connect a real console). Pre-fix `Console.KeyAvailable` was unconditionally polled inside the redraw loop, throwing on a redirected stdin handle. Now `Console.IsInputRedirected` gates the keyboard probe so the polling loop runs without interactive controls when stdin isn't a terminal.

## [0.2.0] - 2026-04-16

### Added
- Manual page (`man 1 peep`) ships with the package and is installed to `share/man/man1/` by scoop and the native installer.

## [0.1.0] - 2026-04-02

- Initial release.
