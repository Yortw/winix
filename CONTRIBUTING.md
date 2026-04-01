# Contributing to Winix

Thanks for your interest in contributing! Winix is a small project and contributions are welcome.

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later
- Git

### Build and Test

```bash
git clone https://github.com/Yortw/winix.git
cd winix
dotnet build Winix.sln
dotnet test Winix.sln
```

### Publish a Native Binary (optional)

```bash
dotnet publish src/timeit/timeit.csproj -c Release -r win-x64
```

Replace `win-x64` with `linux-x64`, `osx-x64`, or `osx-arm64` as needed.

## Project Structure

Winix follows a strict separation between libraries and console apps:

- **Class libraries** (e.g. `Winix.TimeIt`) contain all logic — formatting, orchestration, domain behaviour. These are testable without spawning processes.
- **Console apps** (e.g. `timeit`) are thin entry points — argument parsing, calling the library, writing errors, setting exit codes.
- **Shared library** (`Yort.ShellKit`) provides terminal detection, colour support, and display formatting.

See `CLAUDE.md` for the full project layout and conventions.

## Development Conventions

### Code Style

- **Full braces always** — no braceless `if`/`using`/`foreach`
- **Nullable reference types enabled** — all projects use `<Nullable>enable</Nullable>`
- **Warnings as errors** — the build must be clean
- **AOT-compatible** — no unconstrained reflection, use trim analyzers
- **No top-level statements** — console apps use `namespace`/`class Program`/`static Main`

The `.editorconfig` in the repository root enforces most of these. Your editor should pick them up automatically.

### Testing

We practise TDD where practical: write a failing test, implement the fix, verify it passes. All tools have xUnit test projects under `tests/`.

```bash
dotnet test Winix.sln
```

Tests must pass on all three platforms (Windows, Linux, macOS). The CI pipeline verifies this.

### Output Conventions

- Summary and diagnostic output goes to **stderr** (don't pollute piped stdout)
- Respect the `NO_COLOR` environment variable ([no-color.org](https://no-color.org))
- Structured output (`--json`, `--ndjson`) should be machine-parseable
- Exit codes follow POSIX conventions: 0 = success, 125 = usage error, 126 = not executable, 127 = not found

### Argument Handling

Always use `ProcessStartInfo.ArgumentList` for passing arguments to child processes. Never build argument strings via interpolation or concatenation — it's prone to quoting and escaping bugs, especially on Windows.

## Submitting Changes

1. Fork the repository and create a branch from `main`
2. Make your changes, following the conventions above
3. Add or update tests as appropriate
4. Run `dotnet test Winix.sln` and ensure all tests pass
5. Update the tool's `README.md` if you changed flags, options, or behaviour
6. Open a pull request with a clear description of what and why

### Commit Messages

- Use imperative mood ("Add feature" not "Added feature")
- Keep the subject line under 72 characters
- Explain *why* in the body, not just *what*

### Pull Request Guidelines

- One logical change per PR
- Keep PRs focused — separate refactoring from new features
- The CI pipeline must pass before merge

## Reporting Issues

- **Bugs**: Use the [bug report template](https://github.com/Yortw/winix/issues/new?template=bug_report.md)
- **Features**: Use the [feature request template](https://github.com/Yortw/winix/issues/new?template=feature_request.md)
- **Security**: See [SECURITY.md](SECURITY.md) — do not open public issues for vulnerabilities

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
