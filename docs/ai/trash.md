# trash — AI Agent Guide

**Maturity: fresh** — newer tool, not yet through a stable release; interface may still move. See [../STABILITY.md](../STABILITY.md).

## What This Tool Does

`trash` moves files and directories to the operating system's recycle bin / Trash instead of deleting them permanently, as a single cross-platform native binary with no runtime dependency. It uses the Windows Recycle Bin, the FreeDesktop Trash on Linux, and the macOS Trash. Trashed items remain recoverable through the OS file manager's normal "Put Back" / "Restore" action.

## When to Use This

- Removing files where a permanent `rm` is risky and recovery should remain possible: `trash <path>...`
- Any destructive cleanup in scripts where a mistake should be undoable rather than irreversible
- Cross-platform safe-delete where `trash-cli` (Linux), `macos-trash`, or PowerShell `Remove-Item -Recycle` are not present or not uniform
- Inspecting what is currently in the trash: `trash --list` (add `--json` for machine consumption)
- Purging the trash deliberately: `trash --empty --yes`

## When NOT to Use This

- When you genuinely need a permanent, unrecoverable delete (e.g. secure wipe) — use `rm` / `shred`; `trash` keeps the data in the recycle bin
- When you need to restore a previously trashed item — v1 has no `--restore`; use the OS file manager's Put Back / Restore
- When operating on a filesystem or volume that has no trash location (some network mounts) — the OS may reject the move and `trash` will report a backend failure

## Basic Invocation

```bash
# Move one file to the recycle bin / Trash
trash notes.txt

# Move several files and directories at once
trash old.log build/ tmp.cache

# List current trash contents (table)
trash --list

# Empty the trash, skipping the prompt
trash --empty --yes
```

The plain-mode summary (`trash: moved N item(s) to trash`) goes to **stderr**. On a successful plain trash, nothing goes to stdout. The `--list` table and all `--json` output go to **stdout**.

## JSON Output

Pass `--json` for machine-parseable output:

```bash
trash --list --json
```

Output shape:
```json
{"items": [{"name": "notes.txt", "original_path": "/home/me/notes.txt", "deleted": "2026-05-30T04:12:09Z", "size": 1240, "trash": "home"}]}
```

| Key | Description |
|---|---|
| `name` | File or directory name as stored in the trash. |
| `original_path` | Path the item was deleted from. **Null on macOS** (see Platform Notes). |
| `deleted` | Deletion timestamp, UTC ISO 8601. |
| `size` | Size in bytes. |
| `trash` | Trash location: drive (e.g. `C:`) on Windows; `home` or a mount path on Linux; `home` or a volume on macOS. |

Default (trash) mode emits `{"trashed": N, "failed": [{"path": "…", "error": "…"}]}` — `trashed` is the count sent to the bin/Trash, `failed` lists the paths that failed (empty when all succeeded).

`--empty --json` emits `{"emptied": N, "failed": M}`. `emptied` is the count of items whose data was confirmed removed (**approximate** on Windows — the OS empty API is not per-item attributable); `failed` is the count that could not be removed and, when non-zero, drives exit 1.

## Emptying the Trash — Non-Interactive Safety

`--empty` permanently deletes everything in the trash. Behaviour:

- **Interactive (terminal stdin):** prompts `[y/N]`, defaults to no.
- **Non-interactive (stdin redirected / in a pipe or script):** **refuses without `--yes`**. This prevents an unattended run from wiping the trash by accident.

```bash
# WRONG in a script — refuses and exits 2 (cancelled); nothing is emptied
echo | trash --empty

# CORRECT — explicit consent
trash --empty --yes
```

## Composability

```bash
# Find files and trash them (recoverable bulk cleanup)
files . --name "*.tmp" | wargs trash

# Inspect the trash as JSON and pull out names
trash --list --json | jq -r '.items[].name'
```

## Platform Notes

- **Windows:** uses the Recycle Bin. Trash location reports as the drive letter (e.g. `C:`). `cmd` and PowerShell do **not** expand globs like `*.log` — pass explicit paths or use a globbing shell.
- **Linux:** uses the FreeDesktop Trash spec (`~/.local/share/Trash` plus per-mount `.Trash-<uid>` directories). Trash location reports as `home` or the mount path.
- **macOS:** uses the system Trash. **`--list` shows no original paths** (`original_path` is null) — the Put-Back source is stored by macOS in a private binary store that v1 does not read. `deleted` is approximate (the entry's last-modified time, not the exact trash moment).

## Limitations

- No `--restore` / put-back in v1 — restore through the OS file manager.
- macOS `original_path` is always null in `--list`.
- `--empty` count is approximate.
- Ctrl+C mid-batch is non-atomic; like `rm`, already-trashed items stay trashed.
- No partial-tree rollback beyond the single-item `.trashinfo` cleanup on Linux.

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success. A closed downstream pipe (e.g. `trash --list \| head -1`) also exits 0 — not an error. |
| 2 | `--empty` cancelled — the prompt was declined, or it was refused without `--yes` when not interactive. Nothing was emptied. |
| 1 | One or more paths failed (missing path, permission denied), or some items could not be emptied — partial failure, like `rm`. Stderr carries the per-path message. |
| 125 | Usage error — unknown flag, `--list`/`--empty` misuse, empty or duplicate path, or no paths given. Stderr carries the message. |
| 126 | Backend failure — the OS recycle-bin / Trash API returned an error. Stderr carries the message. |

## Credits

`trash` follows the established native safe-delete tools: [`trash-cli`](https://github.com/andreafrancia/trash-cli) on Linux and [`macos-trash`](https://github.com/sindresorhus/macos-trash) on macOS. It unifies those plus `rm -i` and PowerShell `Remove-Item` behind one cross-platform binary.

## Metadata

Run `trash --describe` for full structured metadata (flags, modes, examples, exit codes).

## Glob expansion on Windows

trash expands `*`/`?` in path positionals itself on Windows (cmd/pwsh don't).
Support matrix: `*` and `?` in any segment — yes; `[...]` — matched literally
(legal filename chars); `**` — usage error (use Git Bash for recursive patterns); no
match — literal passthrough (normal "not found" follows). Quoted args are not
expanded when launched from cmd; PowerShell strips quotes before launch, so
prefer explicit paths there if a literal is required. On Unix the shell expands;
the tool adds nothing. `--describe` exposes this as `glob_expansion`.
