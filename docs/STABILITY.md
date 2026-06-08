# Winix Agent-Surface Stability Policy

This document describes the stability commitments for the **machine-readable surface** of
the Winix tool suite: the `--describe` JSON envelope that every tool emits. Agents and
consumers that parse `--describe` output may rely on the rules below.

**Not covered by this policy:** human-readable text (help prose, error wording, summary
lines), performance characteristics, and undocumented behaviour.

---

## 1. Covered surface

The policy covers the `--describe` JSON envelope emitted by every Winix tool (and every
subcommand that has its own envelope). The contracted fields are:

| Field | Notes |
|---|---|
| `schema_version` | Integer. Versions the envelope structure. |
| `tool` | Tool name string. |
| `version` | Build version string. |
| `maturity` | `"core"` or `"fresh"` (see §4). |
| `description` | One-line tool description. |
| `platform` | Scope, replaces, and per-OS value statements. |
| `options` | Array of option descriptors (long, short, type, description, …). |
| `exit_codes` | Array of `{code, description}` objects. |
| `io` | `{stdin, stdout, stderr}` strings. |
| `examples` | Array of `{command, description}` objects. |
| `composes_with` | Array of `{tool, pattern, description}` objects. |
| `json_output_fields` | Array of `{name, type, description}` objects (present when a tool documents its JSON output schema; not every `--json` tool does). |
| `prefer_default_when` | String array of incumbent-case hints (present only when configured). |
| `usage` | Usage synopsis string (present on all tools). |
| `glob_expansion` | Windows glob-expansion descriptor (present on tools that opt in). |

The `--json` field names, exit-code values, and flag names/shapes across the suite are
also part of the agent surface and subject to the same rules.

---

## 2. Stability rules and deprecation

**Additive changes** (new fields in the envelope, new options, new exit codes with new
meanings) are non-breaking and may land in any release without bumping `schema_version`.

**Breaking changes** (renaming or removing a field, changing a field's type, changing the
meaning of an existing exit code or flag) require a `schema_version` bump and must follow
the deprecation runway below.

**Deprecation runway:** a renamed or removed flag, `--json` field, or envelope field keeps
the old name as a working alias emitting a one-line stderr deprecation notice for at least
**2 minor releases** before the old name is dropped. Where a `--json` or exit-code break
cannot be aliased, a parallel-emission period of the same length applies where feasible.

**Core tools:** the deprecation policy binds strictly.

**Fresh tools:** best-effort — the interface may still move between minor releases (see §4).

---

## 3. `schema_version`: meaning, bump rule, and current value

`schema_version` versions the **structure of the `--describe` envelope** — the field
names, nesting, and types. It does **not** version per-tool content (option lists, exit
codes, descriptions).

- **Additive fields do not bump it.** A new optional field arriving in the envelope is a
  non-breaking additive change; consumers must tolerate unknown fields.
- **Renames, removals, and type changes do bump it.** Any change that breaks a consumer
  parsing the current structure requires incrementing the constant and regenerating
  snapshots.

**Current value: `1`** (defined as `CommandLineParser.DescribeSchemaVersion` in
`src/Yort.ShellKit/CommandLineParser.cs`).

---

## 4. Maturity tiers and promotion

Every tool's `--describe` envelope carries a `maturity` field. There are two tiers:

**`core`** — the tool has completed multi-round review to round-stop AND has survived at
least one stable release in the wild without interface-breaking changes. The deprecation
policy (§2) binds strictly.

**`fresh`** — the tool has been through multi-round review but has not yet been through a
stable release. The interface may still move between minor releases. The deprecation policy
applies best-effort.

**Promotion** from `fresh` to `core` happens when a tool meets the core bar: one stable
release exposure with no interface breaks. Promotion is a one-line `.Maturity()` change
plus a snapshot update, expected the release after initial exposure.

**Current tier assignments (as of v0.4.0):**

- **Core (23):** timeit, squeeze, peep, wargs, files, treex, man, less, whoholds, schedule,
  nc, winix, retry, when, clip, ids, digest, notify, url, qr, protect, unprotect, envvault
- **Fresh (5):** mksecret, trash, hcat, mkauth, demux — reviewed to round-stop 2026-06-07;
  expected promotion at v0.5.0 after first stable exposure

Read the tier from `--describe` at runtime: `jq .maturity <(tool --describe)`.

---

## 5. Enforcement

The `--describe` contract is mechanically enforced by `tests/Winix.Contract.Tests`
(project in `Winix.sln`). The test suite:

- Invokes every `--describe` surface in-process via the library seam.
- Asserts exit 0 and empty stderr.
- Asserts `schema_version` is present and `maturity` is `"core"` or `"fresh"` (a tool
  without a maturity tier cannot ship).
- Compares the normalised JSON output byte-for-byte against committed snapshots under
  `tests/Winix.Contract.Tests/snapshots/`.
- Asserts the registry contains exactly 28 top-level surfaces (one per tool in the suite).

The suite runs in CI on every platform on every push. Any undeclared drift fails the build.

**Making an intentional change:** regenerate snapshots with `WINIX_UPDATE_SNAPSHOTS=1`
(update mode writes the snapshot AND fails the run so CI can never silently self-update),
commit the diff, and bump `CommandLineParser.DescribeSchemaVersion` if the envelope
**structure** changed.

---

## 6. Pre-1.0 honesty

The Winix suite is pre-1.0 (currently 0.x). The contract machinery, tier system, and
enforcement described above exist and run in CI **now**. However, at 0.x the suite reserves
the right to make breaking changes between minor versions. The maturity tiers and
deprecation runway are the good-faith commitment: `core` tools change conservatively and
follow the full deprecation runway; `fresh` tools may move faster. These commitments harden
into a strict guarantee at 1.0.

Consumers integrating `core` tools in stable pipelines can rely on the deprecation runway
today. Consumers integrating `fresh` tools should monitor the `maturity` field and plan for
interface changes until those tools promote to `core`.
