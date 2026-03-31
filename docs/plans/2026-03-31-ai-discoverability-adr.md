# ADR: AI Discoverability for Winix CLI Tools

**Date:** 2026-03-31
**Status:** Proposed
**Context:** Winix tools need to be discoverable and usable by AI coding agents, particularly on Windows where the CLI landscape is poorly represented in LLM training data.
**Related:** [AI Discoverability Design](2026-03-31-ai-discoverability-design.md), [CLI Conventions](2026-03-29-winix-cli-conventions.md)

---

## Decision 1: `--describe` flag for structured tool metadata

**Context:** AI agents currently learn about CLI tools through `--help` text (human prose) or training data. `--help` requires parsing free text to extract flag names, types, and behaviour. Tools not in training data are invisible to agents entirely.

**Decision:** Add a `--describe` flag to every Winix tool that emits a JSON object describing the tool's complete interface: flags, options with types, exit codes, I/O behaviour, usage examples, composability hints, and JSON output field descriptions.

**Rationale:** An agent runs `toolname --describe` once and gets machine-parseable metadata it can use without guessing. The parser already knows all registered flags/options/types, so most of the data is generated automatically — no duplication between `--help` and `--describe`. This is the "machine-readable flag metadata" prescribed by the CLI conventions doc but previously deferred.

**Trade-offs Accepted:**
- Adds a flag to every tool that most human users will never use. Acceptable — it's invisible unless you ask for it, and `--help` already lists many flags.
- JSON schema must be stable (additive only). Acceptable — same contract as `--json` output.
- Fluent builder methods add API surface to `CommandLineParser`. Acceptable — the methods are optional and follow the existing pattern.

**Options Considered:**
- **External metadata files (JSON/YAML alongside the binary):** Rejected — requires distribution of extra files, can get out of sync with the binary, harder for agents to discover.
- **Man page parsing:** Rejected — Windows has no man pages, man format is hostile to machine parsing, and we'd still need to generate man pages.
- **OpenAPI-style schema:** Rejected — overkill for CLI tools. OpenAPI is designed for HTTP APIs with request/response models, not flag-based CLI interfaces.

---

## Decision 2: `llms.txt` at the repo root

**Context:** AI agents browsing a repository or documentation site need a structured entry point to understand what's available. README.md serves humans; there's no machine-oriented equivalent.

**Decision:** Add an `llms.txt` file at the repository root following the [llms.txt convention](https://llmstxt.org/). Lists all tools with one-line descriptions and links to per-tool AI guides. Updated when tools are added.

**Rationale:** Low effort, high signal. The llms.txt convention is gaining adoption and provides a standard location AI agents can check. Even agents unaware of the convention benefit from a concise, structured overview.

**Trade-offs Accepted:**
- One more file to maintain. Acceptable — it's short and changes only when tools are added.
- The llms.txt convention is young and may evolve. Acceptable — the content is plain markdown, trivially updated if the convention changes.

**Options Considered:**
- **Rely on README.md only:** Rejected — README is human-oriented with installation instructions, badges, and formatting that adds noise for agents. `llms.txt` is a clean, focused signal.
- **Claude Code skill files only:** Rejected — Claude-specific format. The guides should work for any AI agent. (Claude skills can reference the guides as a convenience layer.)

---

## Decision 3: Per-tool AI guides in `docs/ai/`

**Context:** `--describe` metadata answers "what can this tool do?" but not "when should I use it?" or "how does it fit into workflows?" Agents need opinionated guidance to choose the right tool and combine tools effectively.

**Decision:** Create `docs/ai/<toolname>.md` for each tool. These are workflow-oriented guides covering: when to use this tool, common patterns, composition with other tools, platform gotchas, and structured output usage. The `llms.txt` file links to these guides.

**Rationale:** Complements `--describe` without duplicating it. The guides provide the "why" and "when" that structured metadata can't express. Written in plain markdown, usable by any AI agent that can read files.

**Trade-offs Accepted:**
- Guides can drift from tool behaviour. Mitigated by keeping guides focused on workflows and gotchas rather than duplicating flag reference (which `--describe` owns).
- More docs to maintain. Acceptable — each guide is 50-100 lines and changes infrequently.

**Options Considered:**
- **Generate guides from `--describe` metadata:** Rejected — generated docs would just reformat the same data. The value is in curated, opinionated workflow guidance that requires human/authorial judgment.
- **Single monolithic guide:** Rejected — agents searching for a specific tool would need to parse a large document. Per-tool files are directly addressable.

---

## Decision 4: Fluent builder API on `CommandLineParser`

**Context:** The `--describe` metadata includes fields the parser doesn't currently track (I/O descriptions, examples, composability). This data needs to come from somewhere.

**Decision:** Add fluent builder methods to `CommandLineParser`: `.StdinDescription()`, `.StdoutDescription()`, `.StderrDescription()`, `.Example()`, `.ComposesWith()`, `.JsonField()`. The console app's `Program.cs` declares all metadata inline alongside flag registration.

**Rationale:** The console app already owns argument parsing and flag registration. Metadata declaration is a natural extension — co-located with the flags it describes, impossible to forget, follows the existing fluent pattern. The class library stays unaware of CLI concerns (consistent with the thin-console-app architecture).

**Trade-offs Accepted:**
- Console app `Program.cs` gets longer with metadata declarations. Acceptable — it's declarative, not logic, and clearly separated from operational code.
- Could have put metadata in the class library as a `ToolMetadata` record. Rejected — the library shouldn't know it's a CLI tool.

**Options Considered:**
- **Metadata in the class library:** Rejected — violates the architecture principle that libraries contain domain logic, not CLI presentation concerns.
- **External metadata file (JSON) loaded at runtime:** Rejected — AOT-hostile (requires file I/O and JSON deserialisation of a resource), can get out of sync, adds deployment complexity.
- **Source generator:** Rejected — over-engineering for declarative metadata that the fluent API handles simply.

---

## Decision 5: Prove on `files`, then retrofit

**Context:** Four tools already exist (timeit, squeeze, peep, wargs). Adding `--describe` support requires ShellKit changes and new metadata on each tool.

**Decision:** Build the `--describe` infrastructure in ShellKit, prove it on the new `files` tool, then retrofit to existing tools in a separate pass.

**Rationale:** The `files` tool is greenfield — we can iterate on the `--describe` format without worrying about backwards compatibility. Once the format is proven and stable, retrofitting is mechanical (add fluent calls to each existing `Program.cs`).

**Trade-offs Accepted:**
- Existing tools temporarily lack `--describe`. Acceptable — they already ship and work. The retrofit is low-risk mechanical work.

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|-------|-------------|
| Suite-level discovery (`winix --list --describe`) | Overlaps with the multi-call binary plan. Will be addressed when that effort starts. |
| Claude Code skill wrappers | The AI guides in `docs/ai/` are agent-agnostic. Claude-specific skills can reference them later as a convenience layer. |
| Man page generation | Separate concern (ShellKit extraction). The `--describe` metadata could feed a man page generator, but that's a future pipeline. |
| `--describe` schema versioning | Not needed until the schema actually changes. When it does, add a `"schema_version"` field. |
