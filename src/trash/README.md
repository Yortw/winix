# trash

Move files to the recycle bin or Trash instead of deleting them — cross-platform, recoverable. Single native binary, no runtime. Uses the OS Recycle Bin (Windows), the FreeDesktop Trash (Linux), and the macOS Trash.

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/trash
```

### Winget (Windows, stable releases)

```bash
winget install Winix.Trash
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.Trash
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).

## Usage

```
trash <path>...        Move one or more files/directories to the recycle bin / Trash.
trash --list           List current trash contents.
trash --empty          Permanently empty the trash.
```

Unlike `rm`, `trash` does not permanently delete: items go to the OS recycle bin / Trash and can be restored through your file manager's normal "Put Back" / "Restore" action.

### Trash files

```bash
# Move a single file to the recycle bin / Trash
trash notes.txt
# trash: moved 1 item(s) to trash

# Move several files and directories at once
trash old.log build/ tmp.cache
# trash: moved 3 item(s) to trash
```

The summary line is written to **stderr**. On a successful plain trash, nothing is written to stdout.

### List trash contents

```bash
# Human-readable table
trash --list

# JSON envelope to stdout
trash --list --json
# {"items":[{"name":"notes.txt","original_path":"/home/me/notes.txt","deleted":"2026-05-30T04:12:09Z","size":1240,"trash":"home"}]}
```

The table shows name, original path, deleted timestamp (UTC ISO 8601), size, and trash location. The trash location is the drive letter on Windows (e.g. `C:`), and `home` or a mount/volume path on Linux/macOS.

### Empty the trash

```bash
# Prompts [y/N] when run interactively
trash --empty

# Skip the prompt
trash --empty --yes

# JSON envelope reports how many items were emptied
trash --empty --yes --json
# {"emptied":42}
```

`--empty` **permanently** deletes everything in the trash. When run interactively it prompts `[y/N]` and defaults to no. When stdin is **not** a terminal (e.g. in a script or pipe), it refuses to proceed without `--yes` / `-y` so an unattended run can never wipe the trash by accident.

## Options

| Flag | Short | Description |
|---|---|---|
| `--list` | | List current trash contents instead of trashing paths. |
| `--empty` | | Permanently empty the trash. |
| `--yes` | `-y` | Skip the `--empty` confirmation prompt. Required to empty non-interactively. |
| `--json` | | Emit a JSON envelope to stdout (works with the default, `--list`, and `--empty`). |
| `--describe` | | Emit structured JSON metadata for AI discoverability. |
| `--help`, `-h` | | Show help and exit. |
| `--version`, `-v` | | Show version and exit. |
| `--color[=auto\|always\|never]` | | Force or suppress coloured output. Bare `--color` = always. Respects `NO_COLOR`. |
| `--no-color` | | Disable coloured output. Respects `NO_COLOR`. |

### JSON fields

Default (trash) mode emits `{"trashed":N,"failed":[…]}`:

| Key | Description |
|---|---|
| `trashed` | Count of paths successfully sent to the recycle bin / Trash. |
| `failed` | Array of `{"path":"…","error":"…"}` objects, one per path that failed; empty when all succeeded. |

`--list` emits an `items` array. Each item has:

| Key | Description |
|---|---|
| `name` | File or directory name as stored in the trash. |
| `original_path` | The path the item was deleted from. Null on macOS (see Known limitations). |
| `deleted` | Deletion timestamp, UTC ISO 8601. |
| `size` | Size in bytes. |
| `trash` | Trash location: drive (e.g. `C:`) on Windows; `home` or a mount path on Linux; `home` or a volume on macOS. |

`--empty` emits `{"emptied":N,"failed":M}`. `emptied` is the count of items whose data was confirmed removed (**approximate** on Windows — the OS empty API is not per-item attributable); `failed` is the count that could not be removed (permission/busy) and, when non-zero, drives exit 1.

## Wildcards on Windows

cmd.exe and PowerShell don't expand `*`/`?` wildcards before starting programs, so
trash expands them itself on Windows — `trash *.log` works the same as in bash.
`*` and `?` work in any path segment. `[...]` is matched literally (brackets are legal
Windows filename characters), and `**` is rejected with an error — use Git Bash for
recursive patterns. A pattern that matches nothing is passed through unchanged,
so you get the normal "not found" error. In cmd, quoting a pattern (`"*.log"`)
suppresses expansion; PowerShell removes quotes before trash sees them, so use `--%`
there if you need a literal. On Linux/macOS your shell expands wildcards as usual and
trash does nothing extra.

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success. A closed downstream pipe (e.g. `trash --list \| head -1`) also exits 0 — it is not an error. |
| 2 | `--empty` cancelled — you declined the confirmation prompt, or it was refused without `--yes` when not interactive. Nothing was emptied. |
| 1 | One or more paths failed (missing path, permission denied), or some items could not be emptied — partial failure, like `rm`. Stderr carries the per-path message. |
| 125 | Usage error — unknown flag, `--list`/`--empty` misuse, empty or duplicate path, or no paths given. Stderr carries the message. |
| 126 | Backend failure — the OS recycle-bin / Trash API returned an error. Stderr carries the message. |

## Colour

`trash` colours its `--list` table and summary lines when stdout/stderr is a terminal. The `--color` and `--no-color` flags control this explicitly, and `NO_COLOR` is respected (no-color.org).

## Known limitations

- **macOS `--list` shows no original paths.** `original_path` is `null` on macOS. The Put-Back source is stored by macOS in a private binary store that v1 does not read.
- **macOS `--list` deletion time is approximate.** On macOS, `deleted` reflects the entry's last-modified time, not the exact moment it was trashed — macOS does not expose a separate trash timestamp to v1.
- **No `--restore` / put-back in v1.** Restore items through your file manager (Windows Recycle Bin, Files/Nautilus, Finder).
- **`--empty` count is approximate.** It counts what was enumerated; the OS empty API may clear bins the listing could not enumerate.
- **Windows glob expansion.** `trash *.log` works on Windows — `trash` expands wildcards itself. See [Wildcards on Windows](#wildcards-on-windows) below.
- **Ctrl+C mid-batch is non-atomic.** Like `rm`, items already moved before the interrupt stay trashed.
- **No partial-tree rollback.** A disk-full or failure mid-move surfaces as a per-path error. There is no rollback beyond the single-item `.trashinfo` cleanup on Linux.

## Credits

`trash` follows the established native tools for safe-delete on each platform:

- [`trash-cli`](https://github.com/andreafrancia/trash-cli) — the de-facto FreeDesktop Trash CLI on Linux.
- [`macos-trash`](https://github.com/sindresorhus/macos-trash) — the established macOS Trash CLI.

It replaces `rm -i`, `trash-cli`, `macos-trash`, and PowerShell `Remove-Item` with one cross-platform binary and consistent flags.

## Related Tools

- [`files`](../files/README.md) — find files to trash: `files . --name "*.tmp" | wargs trash`

## See Also

- `man trash` (after `winix install man`)
- `trash --describe` for JSON metadata
