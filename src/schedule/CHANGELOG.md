# Changelog

All notable changes to **schedule** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/).

## [0.3.0] - 2026-05-10

### Changed (BREAKING)
- Tool-error exit codes now follow the suite-wide POSIX convention (`125` usage / `126` non-runnable / `127` not-found). The legacy generic `1` exit on tool-side errors has been removed; child-process exit codes still pass through unchanged.
- `--json` envelopes for happy-path commands (`add`, `remove`, `list`, `dryrun`, `next`, `history`) now route to **stdout** instead of stderr, per the suite-wide JSON-routing convention. Pre-fix `schedule add … --json | jq` saw an empty pipe because the success envelope was on the wrong stream. Error envelopes still go to stderr (matching gh / kubectl / AWS CLI / jq).
- Cron expressions that have no clean schtasks mapping (e.g. step values, mixed-list day-of-week, complex ranges Windows Task Scheduler can't represent) are now rejected at `add` time rather than being silently degraded into a different schedule on Windows.

### Security
- `schedule add` arguments destined for `schtasks /TR` are now escaped per the Microsoft CRT argument-parsing rules. Pre-fix a command containing `"` or trailing `\` could splice additional tokens into the scheduled action, allowing unintended commands to run on the trigger.
- Crontab argument quoting widened to cover the full POSIX shell metacharacter set (`;`, `&`, `|`, `` ` ``, `$`, `(`, `)`, `<`, `>`, `*`, `?`, `[`, `]`, `~`, `#`, whitespace). Pre-fix a command argument containing one of these characters was written unquoted into the crontab line and reinterpreted by `/bin/sh` at trigger time.
- `schedule add` rejects newline characters in `--name`, command, and arguments. Pre-fix an embedded `\n` would split into a second crontab entry or a second schtasks command line, allowing injection of an additional scheduled task the user never authorised.
- Detached `run` invocations are now wrapped in shell braces so a compound target (`a && b`) cannot escape the foreground/background terminator boundary.

### Fixed
- `crontab -l` failure modes are now distinguished. Pre-fix any non-zero exit from `crontab -l` was treated as "user has no crontab" and `schedule list` returned an empty result; if `schedule add` then ran, the resulting write would silently overwrite a real crontab the tool failed to read. Real failures (cron.deny, PAM, locked spool, missing binary) now surface as `Unavailable` with the underlying stderr text and exit `126`.
- `CrontabParser.AddEntry` for a name that already exists now overwrites the previous entry (matching `schtasks /F` semantics) instead of appending a duplicate. Pre-fix a re-`add` produced two entries with the same name; subsequent `remove` only deleted one of them.
- DST drift in `CronExpression.GetNextOccurrence`. Pre-fix the calculation captured a single UTC offset and reused it across iterations, so the returned wall-clock time was wrong for ~6 months of every year in any DST zone (NZ, AU, EU, US). Now performs wall-clock arithmetic in the target `TimeZoneInfo` with per-candidate offset recompute; spring-forward gap is skipped via `IsInvalidTime`, fall-back uses the .NET default (first occurrence).
- `SchtasksCsvParser` no longer fails on non-en-US date formats. Pre-fix the parser used the invariant culture (`InvariantGlobalization=true` is the AOT default) so dates like `15/06/2026 09:00:00` from a UK / NZ / DE / FR Windows host failed to parse, dropping rows from `schedule list`. Parser now tries the host's regional date format with invariant fallback.
- `schedule list` now surfaces backend failures as a diagnostic warning + exit `126` rather than returning an empty result. Pre-fix an authentication failure, cron daemon error, or trailing-backslash `/TN` query (`\Winix\` on Windows produced "filename syntax is incorrect") was indistinguishable from "no scheduled tasks".
- `schedule next` no longer crashes on unsatisfiable cron expressions (e.g. `0 0 30 2 *`). The `InvalidOperationException` from the underlying iterator is caught and surfaced as a clean diagnostic.
- `schedule run` rejects disabled tasks with a clear "is disabled" message and exit `126`. Pre-fix the disabled gate was bypassed for crontab entries.
- Crontab line lifecycle (`add` / `remove` / `enable` / `disable`) now handles blank-line spacing consistently. Pre-fix a mid-cycle operation against a crontab with readability blank lines could collapse two adjacent entries onto one line, corrupting the schedule.
- `schtasks` invocations that report `ERROR_ELEVATION_REQUIRED` now surface a UAC elevation hint in the error message, instead of a bare exit code.
- Stderr output that arrives on a successful exit is now surfaced as a warning rather than discarded. Pre-fix partial-failure cases (e.g. a single bad row mid-listing) succeeded silently.
- Crontab partial-write failures (write succeeds but the daemon rejects on reload) now produce a specific error message identifying the partial-write state.
- Backend pipe deadlocks fixed in both `schtasks` and `crontab` paths. Pre-fix a child producing more than one OS pipe buffer of stderr while we read only stdout could hang the tool indefinitely; both streams are now drained concurrently with a timeout, and unhandled `Win32Exception` from missing binaries widened to a clean diagnostic.
- `crontab` `run` no longer leaks the child's stdio into the parent's terminal during read.
- Stderr writes guarded against `IOException` (broken pipe) and `ObjectDisposedException` so a closed downstream pipe can't crash the tool mid-emit.
- `--version` output no longer carries the `+gitsha` SourceLink suffix; users now see plain `schedule 0.3.0`, matching the suite-wide convention.

### Added
- `--describe` JSON now advertises the full per-mode field schema. Pre-fix several emitted fields (warning text, failure reason on list, history availability flag) were undocumented, breaking the introspection contract for AI agents and automation.
- Manual page (`man 1 schedule`) now ships with the package and is installed to `share/man/man1/` by scoop and the native installer.

### Internal
- Eight error-message formatters extracted as pure helpers (`FormatRunFailureNullProcess`, `FormatShExit`, `FormatShUnavailable`, `FormatGenericFailure`, `FormatWriteTimeout`, `FormatWriteFailure`, `SchtasksBackend.FormatLaunchFailure`, `FormatTimeoutFailure`) so the message-format paths inside Process-spawning methods are now unit-testable.
- `ScheduleListResult { Available, Tasks, Warning, FailureReason }` widens the list-return shape; `SchtasksBackend` distinguishes benign-empty from real failure via `IsBenignSchtasksEmpty`; `CrontabBackend` propagates `CrontabUnavailableException` as `Unavailable`.
- `CronExpression.GetNextOccurrence` now exposes an internal overload taking `TimeZoneInfo` to allow host-independent DST tests; public API still defaults to `TimeZoneInfo.Local`.

### Documentation
- README, `man 1 schedule`, and the AI agent guide updated to reflect the success-envelope-on-stdout / error-envelope-on-stderr split, the new exit-code conventions, the `Unavailable` list semantics, and the security-relevant escaping rules.
- README colour section corrected to match `Formatting.FormatTable` actual behaviour (pre-fix the documented colour mapping had drifted from the implementation).

## [0.2.0] - 2026-04-16

- Initial release.
