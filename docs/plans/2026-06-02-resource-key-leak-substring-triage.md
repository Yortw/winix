# Pre-sweep substring-dependency triage (plan Task 1b)

Grep: `\.Message\.(Contains|StartsWith|IndexOf|Equals)|\bMessage ==|Assert.*\.Message\b` over `src` + `tests`.

## Production control-flow dependencies on message content (the F3 risk)

| Site | Finding | Verdict |
|---|---|---|
| `src/Winix.Digest/Cli.cs:90-97` | Catches `ArgumentOutOfRangeException`, surfaces `ex.Message` trimmed at the first `\n`. The realistic source is `HmacFactory.cs:94`, which throws with **our own literal English** ("BLAKE2b keyed mode accepts at most 64 bytes per RFC 7693 §2.9. …"). | **SAFE — leave as-is. Do NOT route through SafeError** (would regress the helpful message to "ArgumentOutOfRangeException"). The `IndexOf('\n')` trim is intended for our multi-line message. Latent: `HmacFactory.cs:52` throws the 3-arg `ArgumentOutOfRangeException(nameof(algorithm), …, null)` (CoreLib default message → would leak under the flag), but ArgParser validates the algorithm before `Create`, so it is effectively unreachable. Optional hardening only; not in scope. |
| `src/Winix.TimeIt/Cli.cs:66` | Comment only (references a past `FileNameMissing` fix). timeit's `Cli.cs:89/101` catch **project** exceptions (`CommandNotExecutableException`/`CommandNotFoundException`). | No action — already SAFE per audit. |

**No other production site branches on a framework message substring.** The only F3-class production hit (digest) resolves to "leave alone", which is why the hybrid design matters: a blind find-replace would have regressed it.

## Test assertions on `.Message` (flag-mirroring watch-list)

All located test assertions target **our own / project-wrapped messages or captured child-process stderr**, none of which change under `UseSystemResourceKeys` (the flag only affects CoreLib `SR` messages):

- `clip` (`ex.Message.Contains("busy")`, "xclip", "Can't open display") — `ClipboardException` is a project type / wrapped child stderr.
- `less` InputSourceTests "Is a directory" — project message thrown by `InputSource`.
- `schedule` crontab tests ("PAM authentication failed", "spool locked", "forge") — captured crontab child stderr wrapped in project exceptions.
- `winix` manifest tests ("version", "tools", "download"), adapter tests ("scoop bucket add", "brew tap", "exit code 1") — project `ManifestParseException` / adapter messages.
- `protect` ("Linux", "macOS", "magic", "version", "platform", "integrity", "consistency error", namespace/key) — project messages.
- `digest` HmacFactoryTests "64 bytes" — our literal English (`HmacFactory.cs:94`), unaffected by the flag.
- `envvault` ("USER", "not valid UTF-8"), `timeit` ("failed to start"), `wargs` ("real cause"), `retry` ("definitely-a-bug"), `nc` ("simulated send failure") — project/constructed messages.

**Conclusion:** no test is predicted to break from flag-mirroring. Each tool's task must still run its full test project after adding the flag and fix any surprise (assertion-text only), but there is no known control-flow defect to fix beyond confirming the digest site stays as-is.

## Bonus
- `qr` tests already assert `DoesNotContain("Arg_ParamName_Name", ex.Message)` — these currently pass **trivially** (JIT resolves English). Once `qr.Tests` gains `UseSystemResourceKeys=true`, they become **meaningful** resource-key guards. Net improvement, no change needed.

## `when` special case
`when.Tests` keeps `InvariantGlobalization=false` (ICU needed for TZ/month parsing) and gains `UseSystemResourceKeys=true`. The ICU-on + system-resource-keys-on combination is untested — run when's tests with both set and record the result before committing its csproj change.
