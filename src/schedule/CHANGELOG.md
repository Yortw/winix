# Changelog

All notable changes to **schedule** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/).

## [0.3.0] - 2026-05-10

### Fixed
- `--json` envelopes for happy-path commands (`add`, `remove`, `list`, `dryrun`) now route to **stdout** instead of stderr, per the suite-wide JSON-routing convention. Pre-fix `schedule add … --json | jq` saw an empty pipe because the success envelope was on the wrong stream. Error envelopes still go to stderr.

### Documentation
- README, man.1, and AI agent guide updated to reflect the success-envelope-on-stdout / error-envelope-on-stderr split.

## [0.2.0] - 2026-04-16

- Initial release.
