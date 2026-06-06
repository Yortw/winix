# nc Cli.RunAsync Seam Retrofit Implementation Plan (seam phase 4, final)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Retrofit the byte-stream `Cli.RunAsync` seam to nc — the last seamless tool — strictly behaviour-neutral, with two-stage tests and the NX1 fixture case. Completes the seam class (27/27).

**Architecture:** Move `src/nc/Program.cs` orchestration into `src/Winix.NetCat/Cli.cs` per `docs/plans/2026-06-06-nc-cli-seam-design.md` + ADR (N1–N6). Signature: `RunAsync(string[] args, Stream stdin, Stream stdout, TextWriter stderr, CancellationToken)`. Check mode wraps stdout in a leaveOpen UTF-8 writer (N1). Main keeps `ConsoleEnv` + CTS/`CancelKeyPress` (relocated from mid-stack `DispatchAsync` — N3).

**Tech Stack:** .NET 10, C#, xUnit, Yort.ShellKit; real loopback sockets in tests.

**Branch:** `feature/cli-seam-nc` (created; design + ADR committed as `f1fa080`).

**Hard rules for executors:**
- **Existing tests pass UNMODIFIED.** Pre-refactor: **124 passed, 0 skipped** in `Winix.NetCat.Tests`. Sole authorised exceptions (comment-only, zero assertion changes, noted in commit message): (a) `tests/Winix.NetCat.Tests/ColorTests.cs:14` — its "nc has no Cli.Run library seam — Program.cs is an async entry point…" seam-note becomes FALSE after this change; rewrite that comment to describe the new seam reality; (b) stale `Program.cs` location references in `tests/Winix.NetCat.Tests/ProgramMainTests.cs` (~lines 13, 108) — retarget wording. Anything else = STOP, BLOCKED.
- **Stream ownership (ADR N2):** `Cli.RunAsync` NEVER disposes `stdin`/`stdout`; the check-mode `StreamWriter` is `leaveOpen: true` and flushed before return.
- **No blocking-on-async** anywhere new; `ConfigureAwait(false)` usages move as-is.
- Preserve every moved comment VERBATIM (round-1 through round-3 + tier-1 findings); only file/location references inside comments may be updated.
- `Winix.NetCat` already references Yort.ShellKit and grants `InternalsVisibleTo("Winix.NetCat.Tests")` — no csproj changes.
- Full braces; XML docs on public members; Bash tool blocks `&&`/`;`; commit per task; no Co-Authored-By.
- **Probed contracts (2026-06-06, linux-x64 binary) — pinned, do not soften:**
  - SIGINT during listen-accept, `--json`: stderr = the plain line `nc: interrupted` FOLLOWED BY the envelope `{"tool":"nc","version":"…","mode":"listen","port":N,"protocol":"tcp","tls":false,"exit_code":130,"exit_reason":"interrupted","error":"user cancelled"}` — **unlike wargs, the text line precedes the envelope: tests parse the LAST non-empty stderr line as JSON.** Human mode: just `nc: interrupted`. Exit 130 (fixture's exit file shows GNU timeout's own 124).
  - DNS-failure check-scan: exit 1, stderr `nc: all 1 port probes failed: <OS-SPECIFIC TEXT> (use --verbose for per-port detail)` — the resolver text differs per OS ("Name or service not known" Linux vs "No such host is known"-class Windows): **assert the stable prefix `port probes failed` only, never the OS text.**
- **W4 two-stage discipline:** Task 2 = wiring only; Task 4 = newly-unlocked, only after Task 3's gates.

---

### Task 0: Baseline capture (stream-separated) + test count

- [ ] **Step 1: Build + capture**

```bash
dotnet build /d/projects/winix/src/nc/nc.csproj --nologo -v quiet
mkdir -p /d/projects/winix/tmp/seam-baseline
/d/projects/winix/src/nc/bin/Debug/net10.0/nc.exe --help > /d/projects/winix/tmp/seam-baseline/nc-help.out 2> /d/projects/winix/tmp/seam-baseline/nc-help.err
/d/projects/winix/src/nc/bin/Debug/net10.0/nc.exe --describe > /d/projects/winix/tmp/seam-baseline/nc-describe.out 2> /d/projects/winix/tmp/seam-baseline/nc-describe.err
```
Expected: `.out` non-empty, `.err` empty (emptiness is baseline — F1 routing gate).

- [ ] **Step 2: Count** — `dotnet test /d/projects/winix/tests/Winix.NetCat.Tests/Winix.NetCat.Tests.csproj --nologo -v quiet` → 124 passed, 0 skipped.

---

### Task 1: Create `Winix.NetCat/Cli.cs` + `UsageException.cs`, thin `Program.cs`

**Files:**
- Create: `src/Winix.NetCat/Cli.cs`
- Create: `src/Winix.NetCat/UsageException.cs` (relocated from Program.cs — ADR N4)
- Rewrite: `src/nc/Program.cs` (468 lines → ~40)
- Comment-only fixups: `tests/Winix.NetCat.Tests/ColorTests.cs`, `tests/Winix.NetCat.Tests/ProgramMainTests.cs`

Read ALL of `src/nc/Program.cs` first. Sibling exemplars: `src/Winix.Wargs/Cli.cs` (catches-in-seam), `src/Winix.Peep/Cli.cs` (async) — nc's plan is authoritative.

- [ ] **Step 1: Create `src/Winix.NetCat/UsageException.cs`**

```csharp
#nullable enable

namespace Winix.NetCat;

/// <summary>
/// Signals an argument/option combination the CLI rejects with a usage error (exit 125).
/// Thrown by <see cref="Cli"/>'s option validation; relocated from the console app when the
/// validation moved into the library (seam ADR N4).
/// </summary>
internal sealed class UsageException : System.Exception
{
    /// <summary>Creates the exception with the user-facing message (printed verbatim).</summary>
    public UsageException(string message) : base(message) { }
}
```

(Internal + existing `InternalsVisibleTo` covers tests; the app never throws/catches it directly post-move. Delete the old class from Program.cs.)

- [ ] **Step 2: Create `src/Winix.NetCat/Cli.cs`**

Skeleton (`…` = moved verbatim per the mapping table):

```csharp
#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Yort.ShellKit;

namespace Winix.NetCat;

/// <summary>
/// Library entry point for the nc tool: parses arguments, validates option combinations,
/// and dispatches connect / listen / check modes with the supplied byte streams and writer.
/// <c>Program.Main</c> is a thin shell owning console setup and Ctrl+C registration.
/// </summary>
/// <remarks>
/// Stream ownership (seam ADR N2): this method NEVER disposes <paramref name="stdin"/> or
/// <paramref name="stdout"/> — the caller owns their lifetime (production passes the
/// process-lifetime console streams; tests pass MemoryStreams). Check mode's text output
/// (the open-port list) is written through an internal leaveOpen UTF-8 writer over
/// <paramref name="stdout"/>, flushed before return (seam ADR N1) — byte-identical to the
/// previous Console.Out path under ConsoleEnv.UseUtf8Streams.
/// </remarks>
public static class Cli
{
    /// <summary>
    /// Runs the nc CLI.
    /// </summary>
    /// <param name="args">Raw command-line arguments.</param>
    /// <param name="stdin">Bytes to send to the peer (connect/listen modes). Not used by check mode.</param>
    /// <param name="stdout">Receives bytes from the peer (connect/listen) or the open-port
    /// text list (check mode, via the internal writer).</param>
    /// <param name="stderr">Status messages, errors, and the <c>--json</c> summary.</param>
    /// <param name="cancellationToken">Cancellation signal (Ctrl+C in production, owned by Main).
    /// User-cancel surfaces as exit 130 with the "interrupted" line/envelope.</param>
    /// <returns>0 success; 1 refused/DNS/closed; 2 timeout; 125 usage; 126 permission/unexpected; 130 interrupted.</returns>
    public static async Task<int> RunAsync(string[] args, Stream stdin, Stream stdout,
        TextWriter stderr, CancellationToken cancellationToken)
    {
        string version = GetVersion();
        // … parser chain verbatim from Program.cs …
        // ParseResult result = parser.Parse(args);
        // if (result.IsHandled) { return result.ExitCode; }
        // if (result.HasErrors) { return result.WriteErrors(stderr); }
        // … BuildOptions try/catch (UsageException -> FormatErrorLine to stderr, 125) verbatim …
        // try { return await RunCoreAsync(options, version, stdin, stdout, stderr, cancellationToken).ConfigureAwait(false); }
        // catch (OperationCanceledException) { … verbatim from Main: "nc: interrupted" + optional envelope to stderr; return 130 … }
        // catch (Exception ex) when (…) { … verbatim from Main: UnwrapTypeInit + msg + optional envelope; return 126 … }
    }

    // private static NetCatOptions BuildOptions(ParseResult result, string version) { … verbatim … }
    // private static async Task<int> RunCoreAsync(NetCatOptions options, string version,
    //     Stream stdin, Stream stdout, TextWriter stderr, CancellationToken cancellationToken)
    // { … DispatchCoreAsync body, transformed per the mapping table … }
    // … TryWriteJson / UnwrapTypeInit / GetVersion verbatim …
}
```

**Transformation mapping table:**

| Old (`src/nc/Program.cs`) | New (`Cli.cs`) |
|---|---|
| Main's catch arms (OCE → "nc: interrupted" + envelope + 130; catch-all → 126) incl. their comments | `Cli.RunAsync`'s catches; `Console.Error` → `stderr` |
| `result.WriteErrors(Console.Error)` / the `UsageException` catch's `Console.Error.WriteLine` | `stderr` |
| `DispatchAsync` (CTS + `CancelKeyPress` + try/finally) | **DISSOLVES — registration moves to Main (ADR N3)**; the seam receives the token |
| `DispatchCoreAsync(options, version, cts)` | `RunCoreAsync(options, version, stdin, stdout, stderr, cancellationToken)`; internal `cts.Token` usages → `cancellationToken` |
| `Stream stdin = Console.OpenStandardInput(); Stream stdout = Console.OpenStandardOutput(); TextWriter stderr = Console.Error;` (top of DispatchCoreAsync) | **DELETED** — they arrive as parameters |
| Check-mode `Console.Out.WriteLine(Formatting.FormatOpenPortLine(…))` | a check-mode-local writer: `using var stdoutText = new StreamWriter(stdout, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true); stdoutText.WriteLine(…)` per open port, with `stdoutText.Flush()` before returning from the check arm. Declare it INSIDE the `case NetCatMode.Check` block so connect/listen never construct it. (ADR N1; the dispose of a leaveOpen writer only flushes — wrap in `using` for tidiness.) |
| `checker.CheckAsync(…, cts.Token, …)` / `listener.RunAsync(…, cts.Token)` / `client.RunAsync(…, cts.Token)` | `…, cancellationToken)` — the engines already take the streams; pure threading |
| `TryWriteJson`, `UnwrapTypeInit`, `GetVersion` | move verbatim (`GetVersion` already anchors `typeof(NetCatOptions).Assembly`) |
| `internal sealed class UsageException` at the bottom of Program.cs | DELETED (relocated in Step 1) |

All round-fix comments (round-1 C3/I-1/I-5, round-2 C1/C3/I7, round-3 C1/CR-I1/CR-I6/SFH-I3/SFH-I5) move **verbatim**.

- [ ] **Step 3: Rewrite `src/nc/Program.cs`** — complete replacement:

```csharp
#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Winix.NetCat;
using Yort.ShellKit;

namespace Nc;

internal sealed class Program
{
    /// <summary>
    /// Entry point. Owns process-global state only: console setup and Ctrl+C registration.
    /// All parsing, validation, and mode dispatch live in <see cref="Cli.RunAsync"/>.
    /// </summary>
    static async Task<int> Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        // Named handler + finally-unregister so Ctrl+C arriving during shutdown can't fire a
        // handler that calls Cancel on a disposed CTS. Same pattern as retry/envvault.
        // Registration moved here from the old DispatchAsync (seam ADR N3): process-global
        // console state is Main's responsibility; the seam observes only the token.
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            try { cts.Cancel(); } catch (ObjectDisposedException) { /* raced with shutdown */ }
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            return await Cli.RunAsync(args, Console.OpenStandardInput(), Console.OpenStandardOutput(),
                Console.Error, cts.Token).ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }
}
```

- [ ] **Step 4: Build** — 0 warnings. **Step 5:** suite → 124/0 unchanged; apply the two authorised comment fixups (ColorTests seam-note rewrite + ProgramMainTests location refs). **Step 6: Commit**

```bash
git -C /d/projects/winix add src/Winix.NetCat/Cli.cs src/Winix.NetCat/UsageException.cs src/nc/Program.cs tests/Winix.NetCat.Tests/ColorTests.cs tests/Winix.NetCat.Tests/ProgramMainTests.cs
git -C /d/projects/winix commit -m "refactor(nc): extract byte-stream Cli.RunAsync library seam — completes the seam class (27/27 tools)

Behaviour-neutral move per docs/plans/2026-06-06-nc-cli-seam-design.md.
Check mode's port list now flows through a leaveOpen UTF-8 writer over the
stdout Stream (ADR N1); streams are caller-owned, never disposed (N2);
CTS/CancelKeyPress relocated from DispatchAsync to Main (N3); UsageException
relocated to the library (N4). Comment-only test fixups: ColorTests'
no-seam note (now false) + ProgramMainTests location refs."
```

---

### Task 2: Stage-1 wiring tests

**Files:**
- Create: `tests/Winix.NetCat.Tests/CliRunAsyncTests.cs`

- [ ] **Step 1: Write the tests** (reuse the suite's existing free-port helper if one exists — check `tests/Winix.NetCat.Tests` for the loopback-listener pattern its socket tests already use; otherwise use the bind-port-0-read-back idiom shown below):

```csharp
#nullable enable

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Yort.ShellKit;

namespace Winix.NetCat.Tests;

/// <summary>
/// Stage-1 wiring tests for <see cref="Cli.RunAsync"/> (seam ADR N6): the BuildOptions usage
/// matrix, check mode against real loopback sockets, and the N1 writer-wrap byte pin.
/// Newly-unlocked byte-path and cancellation tests live in CliRunAsyncUnlockedTests (stage 2).
/// </summary>
public class CliRunAsyncTests
{
    private static async Task<(int Exit, byte[] Stdout, string Stderr)> RunCliAsync(
        byte[]? stdinBytes, params string[] args)
    {
        using var stdin = new MemoryStream(stdinBytes ?? Array.Empty<byte>());
        using var stdout = new MemoryStream();
        var stderr = new StringWriter();
        int exit = await Cli.RunAsync(args, stdin, stdout, stderr, CancellationToken.None);
        return (exit, stdout.ToArray(), stderr.ToString());
    }

    /// <summary>Starts a real TCP listener on an OS-assigned loopback port; returns (listener, port).
    /// Caller stops the listener.</summary>
    private static (TcpListener Listener, int Port) StartLoopbackListener()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return (listener, port);
    }

    /// <summary>Returns a loopback port that is (momentarily) closed: bind, read, release.
    /// Inherent TOCTOU is acceptable — reuse within the test's microseconds is implausible.</summary>
    private static int GetClosedPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    // --- BuildOptions usage matrix → 125 ---

    [Theory]
    [InlineData("--listen", "--check", "h", "80")]
    [InlineData("--tls", "--udp", "h", "80")]
    [InlineData("--tls", "--listen", "80")]
    [InlineData("--insecure", "h", "80")]
    [InlineData("--bind", "127.0.0.1", "h", "80")]
    [InlineData("--listen", "--bind", "not-an-ip", "80")]
    [InlineData("--listen", "--ipv4", "--bind", "::1", "80")]
    [InlineData("--listen", "--ipv6", "--bind", "127.0.0.1", "80")]
    [InlineData("--ipv4", "--ipv6", "h", "80")]
    [InlineData("--verbose", "h", "80")]
    [InlineData("--no-shutdown", "--check", "h", "80")]
    [InlineData("--listen", "80", "extra")]
    [InlineData("h")]
    [InlineData("-z", "h", "not-a-port")]
    [InlineData("h", "80-90")]
    public async Task UsageMatrix_Returns125_NothingOnStdout(params string[] args)
    {
        var r = await RunCliAsync(null, args);
        Assert.Equal(ExitCode.UsageError, r.Exit);
        Assert.NotEqual(string.Empty, r.Stderr);
        Assert.Empty(r.Stdout);
    }

    // --- Check mode vs real loopback sockets ---

    [Fact]
    public async Task Check_OpenPort_ExactBytesOnStdout_ExitZero()
    {
        var (listener, port) = StartLoopbackListener();
        try
        {
            var r = await RunCliAsync(null, "-z", "127.0.0.1", port.ToString());
            Assert.Equal(0, r.Exit);
            // The N1 writer-wrap byte pin: exact bytes incl. newline, identical to the old
            // Console.Out path under UseUtf8Streams (UTF-8, no BOM, Environment.NewLine).
            // VERIFY the literal text against Formatting.FormatOpenPortLine at implementation
            // (colour off → expected shape "{port} open").
            byte[] expected = Encoding.UTF8.GetBytes($"{port} open" + Environment.NewLine);
            Assert.Equal(expected, r.Stdout);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task Check_ClosedPort_Exit1_EmptyStdout()
    {
        int port = GetClosedPort();
        var r = await RunCliAsync(null, "-z", "127.0.0.1", port.ToString());
        Assert.Equal(1, r.Exit);
        Assert.Empty(r.Stdout);
    }

    [Fact]
    public async Task Check_ClosedPort_Verbose_LineOnStderr()
    {
        int port = GetClosedPort();
        var r = await RunCliAsync(null, "-z", "-v", "127.0.0.1", port.ToString());
        Assert.Equal(1, r.Exit);
        Assert.Contains(port.ToString(), r.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_Json_EnvelopeOnStderr_StdoutStaysEmpty()
    {
        // Round-3 CR-I6 pin: under --json the open-port TEXT lines are suppressed — stdout
        // must stay byte-empty; the envelope (with ports[]) goes to stderr.
        var (listener, port) = StartLoopbackListener();
        try
        {
            var r = await RunCliAsync(null, "-z", "--json", "127.0.0.1", port.ToString());
            Assert.Equal(0, r.Exit);
            Assert.Empty(r.Stdout);
            using var doc = JsonDocument.Parse(r.Stderr);
            Assert.Equal("check", doc.RootElement.GetProperty("mode").GetString());
            Assert.Equal(0, doc.RootElement.GetProperty("exit_code").GetInt32());
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task Check_DnsFailure_SummaryOnStderr_Exit1()
    {
        // Round-1 I-5 pin. The resolver text is OS-SPECIFIC ("Name or service not known" on
        // Linux, different on Windows) — assert only the stable prefix, never the OS text
        // (probed 2026-06-06).
        var r = await RunCliAsync(null, "-z", "winix-invalid-host-zz9.invalid", "80");
        Assert.Equal(1, r.Exit);
        Assert.Contains("port probes failed", r.Stderr, StringComparison.Ordinal);
        Assert.Empty(r.Stdout);
    }

    [Fact]
    public async Task Check_MixedOpenClosed_OpenLinePrinted_Exit1()
    {
        var (listener, openPort) = StartLoopbackListener();
        try
        {
            int closedPort = GetClosedPort();
            var r = await RunCliAsync(null, "-z", "127.0.0.1", $"{openPort},{closedPort}");
            Assert.Equal(1, r.Exit); // worst status wins
            Assert.Contains($"{openPort} open", Encoding.UTF8.GetString(r.Stdout), StringComparison.Ordinal);
        }
        finally { listener.Stop(); }
    }
}
```

- [ ] **Step 2: Run** (`--filter "FullyQualifiedName~CliRunAsyncTests"`) — all pass; the `// VERIFY` open-port literal: fix the ASSERTION text if `FormatOpenPortLine`'s shape differs (record it); production-defect suspicion = STOP.
- [ ] **Step 3: Full suite** — 124 + 21 ≈ 145 shape, 0 failed. **Step 4: Commit**

```bash
git -C /d/projects/winix add tests/Winix.NetCat.Tests/CliRunAsyncTests.cs
git -C /d/projects/winix commit -m "test(nc): Cli.RunAsync stage-1 wiring tests — usage matrix, check mode vs real loopback sockets, N1 writer-wrap byte pin (W4 stage 1)"
```

---

### Task 3: Byte-stability verification (neutrality gate — no commit)

- [ ] **Step 1:** rebuild; capture `-after` per-stream; 4 independent diffs vs Task 0 (same shape as prior phases). Zero diff on all four; `.out`↔`.err` migration = STOP.
- [ ] **Step 2: Manual smoke**

```bash
bash -c '/d/projects/winix/src/nc/bin/Debug/net10.0/nc.exe -z 127.0.0.1 65000; echo EXIT=$?'
bash -c '/d/projects/winix/src/nc/bin/Debug/net10.0/nc.exe -z --json 127.0.0.1 65000 1>/dev/null; echo EXIT=$?'
```
Expected: first → exit 1 (closed), no output (or refused-fast); second → JSON envelope on stderr, exit 1.

---

### Task 4: Stage-2 newly-unlocked tests (only after Task 3 passes)

**Files:**
- Create: `tests/Winix.NetCat.Tests/CliRunAsyncUnlockedTests.cs`

- [ ] **Step 1: Write the tests**

```csharp
#nullable enable

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Winix.NetCat.Tests;

/// <summary>
/// Stage-2: the previously-impossible coverage the byte-stream seam unlocks (seam ADR N6) —
/// the full in-process byte path (MemoryStream ↔ real loopback socket) for connect and listen,
/// and deterministic cancellation envelopes. Added after stage-1 neutrality validation.
/// </summary>
public class CliRunAsyncUnlockedTests
{
    /// <summary>Parses the LAST non-empty stderr line as JSON. Probed 2026-06-06: nc's cancel
    /// path writes the plain "nc: interrupted" line BEFORE the envelope (unlike wargs's
    /// envelope-only discipline) — whole-buffer parsing would throw.</summary>
    private static JsonDocument ParseLastLine(string stderr)
    {
        string[] lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return JsonDocument.Parse(lines[lines.Length - 1]);
    }

    // --- Connect-mode byte path ---

    [Fact]
    public async Task Connect_BytePath_NonUtf8BytesRoundTrip()
    {
        // In-process echo server: accept one connection, read to EOF (peer half-close),
        // echo everything back, close. The payload includes bytes invalid as UTF-8 —
        // proving the seam carries BYTES, not text.
        byte[] payload = { 0x00, 0x01, 0xFF, 0xFE, 0x80, 0x47, 0x45, 0x54, 0x0A, 0xC0 };
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task serverTask = Task.Run(async () =>
        {
            using TcpClient peer = await listener.AcceptTcpClientAsync();
            using NetworkStream ns = peer.GetStream();
            using var buffer = new MemoryStream();
            await ns.CopyToAsync(buffer); // reads until nc half-closes after stdin EOF
            buffer.Position = 0;
            await buffer.CopyToAsync(ns);
            // close → nc sees remote EOF → exits
        });

        try
        {
            using var stdin = new MemoryStream(payload);
            using var stdout = new MemoryStream();
            var stderr = new StringWriter();
            int exit = await Cli.RunAsync(
                new[] { "--json", "127.0.0.1", port.ToString() }, stdin, stdout, stderr, CancellationToken.None);
            await serverTask;

            Assert.Equal(0, exit);
            Assert.Equal(payload, stdout.ToArray());
            using var doc = ParseLastLine(stderr.ToString());
            Assert.Equal("connect", doc.RootElement.GetProperty("mode").GetString());
            Assert.Equal(payload.Length, doc.RootElement.GetProperty("bytes_sent").GetInt32());
            Assert.Equal(payload.Length, doc.RootElement.GetProperty("bytes_received").GetInt32());
        }
        finally { listener.Stop(); }
    }

    // --- Listen-mode byte path ---

    [Fact]
    public async Task Listen_BytePath_ReceivesClientBytesOnStdout()
    {
        // Seam listens on an OS-assigned... nc takes an explicit port; pick one via
        // bind-and-release (TOCTOU acceptable in-test), then race-tolerantly retry once.
        byte[] payload = { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x0A };
        int port = GetProbablyFreePort();

        using var stdin = new MemoryStream(); // nothing to send
        using var stdout = new MemoryStream();
        var stderr = new StringWriter();
        Task<int> ncTask = Cli.RunAsync(
            new[] { "--json", "-l", port.ToString() }, stdin, stdout, stderr, CancellationToken.None);

        // Connect after a short readiness wait (the listener binds synchronously early in
        // RunAsync; poll-connect handles the startup race without a fixed sleep).
        using (var client = new TcpClient())
        {
            await ConnectWithRetryAsync(client, port, attempts: 50, delayMs: 100);
            using NetworkStream ns = client.GetStream();
            await ns.WriteAsync(payload);
        } // dispose closes → nc sees EOF → exits

        int exit = await ncTask;
        Assert.Equal(0, exit);
        Assert.Equal(payload, stdout.ToArray());
        using var doc = ParseLastLine(stderr.ToString());
        Assert.Equal("listen", doc.RootElement.GetProperty("mode").GetString());
    }

    private static int GetProbablyFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static async Task ConnectWithRetryAsync(TcpClient client, int port, int attempts, int delayMs)
    {
        for (int i = 0; ; i++)
        {
            try { await client.ConnectAsync(IPAddress.Loopback, port); return; }
            catch (SocketException) when (i < attempts - 1) { await Task.Delay(delayMs); }
        }
    }

    // --- Cancellation (probed contracts — failures are real signals, do not soften) ---

    [Fact]
    public async Task Listen_PreCancelledToken_130_InterruptedEnvelope()
    {
        using var stdin = new MemoryStream();
        using var stdout = new MemoryStream();
        var stderr = new StringWriter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        int exit = await Cli.RunAsync(
            new[] { "--json", "-l", GetProbablyFreePort().ToString() }, stdin, stdout, stderr, cts.Token);
        Assert.Equal(130, exit);
        Assert.Contains("nc: interrupted", stderr.ToString(), StringComparison.Ordinal);
        using var doc = ParseLastLine(stderr.ToString());
        Assert.Equal("interrupted", doc.RootElement.GetProperty("exit_reason").GetString());
        Assert.Equal(130, doc.RootElement.GetProperty("exit_code").GetInt32());
    }

    [Fact]
    public async Task Listen_MidAcceptCancel_130_Promptly()
    {
        // The deterministic hanging case (probed): listen-accept waits forever; cancel at
        // 300ms must abort it. Liveness bound is coarse (not perf).
        using var stdin = new MemoryStream();
        using var stdout = new MemoryStream();
        var stderr = new StringWriter();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(300);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int exit = await Cli.RunAsync(
            new[] { "-l", GetProbablyFreePort().ToString() }, stdin, stdout, stderr, cts.Token);
        sw.Stop();
        Assert.Equal(130, exit);
        Assert.Contains("nc: interrupted", stderr.ToString(), StringComparison.Ordinal);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15),
            $"cancel took {sw.Elapsed} — accept was not aborted promptly");
    }
}
```

- [ ] **Step 2: Run + full suite** — all pass (124 + 21 + 4 ≈ 149 shape). Pinned cancellation contracts: STOP on failure. The listen-mode port race: if `Listen_BytePath…` proves flaky on the free-port TOCTOU, switch to a retry-the-whole-test loop over 3 fresh ports and record the deviation.
- [ ] **Step 3: Commit**

```bash
git -C /d/projects/winix add tests/Winix.NetCat.Tests/CliRunAsyncUnlockedTests.cs
git -C /d/projects/winix commit -m "test(nc): newly-unlocked seam coverage — in-process byte path (connect echo + listen receive, non-UTF-8 payloads) and deterministic interrupted envelopes (W4 stage 2)"
```

---

### Task 5: Fixture NX1 (cancellation-smoke pattern, 4th adopter)

**Files:**
- Modify: the tracked nc fixture (locate: `git -C /d/projects/winix ls-files "artifacts/*nc*"`; expected `artifacts/round-stop-2026-05-09/nc/run-smokes.sh`; commit with `git add -f`)

- [ ] **Step 1: Add the case** (after the last existing case, matching the file's helper conventions):

```bash
# ── Capability-surface addition (2026-06-06) ──
# NX1: SIGINT during listen-accept — the interrupted contract end-to-end.
# EXPECTED RESULT: exit file = 124 AND stderr = the line `nc: interrupted` followed by the
# envelope {"...,"exit_code":130,"exit_reason":"interrupted","error":"user cancelled"}.
# (nc's cancel stderr is text-line THEN envelope — unlike wargs's envelope-only; probed
# 2026-06-06 on the linux-x64 binary, exit ~30ms after the INT, nothing lingers.)
# 124 is GNU timeout's OWN code. Linux-only: MSYS cannot deliver SIGINT to native exes;
# covered on Windows by the seam cancellation tests.
if [ "$(uname -s)" = "Linux" ]; then
  run NX1 "SIGINT during listen-accept -> interrupted envelope (exit 124 = timeout's own code)" timeout -s INT 2 "$BIN" --json -l 18097
else
  echo "=== NX1: SKIPPED (Windows: no SIGINT delivery to native exe from this harness) ==="
  echo "skipped" > "$OUT/NX1.exit"
fi
```
(Adapt the helper name/OUT var to the fixture's actual conventions; if its `run` helper's outer timeout is shorter than 5s, verify nesting still leaves room for the 2s INT.)

- [ ] **Step 2:** republish win-x64 → refresh `fresh-publish/nc.exe` → run fixture on Windows (NX1 SKIP, all existing cases unchanged vs their documented contracts).
- [ ] **Step 3:** republish linux-x64 in WSL → retargeted copy in tmp/ → run → NX1 exit file 124 + the probed stderr shape; compare all case exits Windows↔Linux.
- [ ] **Step 4: Commit** (`git add -f`, message per the WX1/R08/P05 precedent).

---

### Task 6: Wrap-up

- [ ] Full `Winix.sln` test → 0 failures (Trash flake: isolate-re-run before treating as real).
- [ ] CLAUDE.md layout: `src/Winix.NetCat/` line — locate it (the layout list may name it under nc); append `, Cli.RunAsync seam` to the library's parenthetical (match exact current text).
- [ ] Commit; push `-u origin feature/cli-seam-nc`; `gh workflow run ci.yml --ref feature/cli-seam-nc`.

---

### Task 7: Main-session gates (NOT for subagents)

- [ ] WSL full `Winix.NetCat.Tests` run (cancellation + socket tests under Linux).
- [ ] 3-OS CI green on the branch.
- [ ] Whole-feature fresh-eyes review (full branch diff).
- [ ] Merge `--no-ff` into `release/v0.4.0`; post-merge CI watch; delete branch; memory update — **backlog 1 → 0, seam class COMPLETE (27/27)**; note the `UnwrapTypeInit`-to-ShellKit quality follow-up in the backlog.

---

## Self-review record (plan author, 2026-06-06)

- **Spec coverage:** N1 → Task 1 mapping row + Task 2's exact-bytes pin; N2 → hard rule + XML remarks; N3 → Task 1 Main listing; N4 → Task 1 Step 1; N5 → recorded as a deliberate non-test (no timeout seam test anywhere — by decision); N6 → two-stage split + the probed contracts block.
- **Placeholder scan:** `…` markers are mapping-table-backed; one `// VERIFY` (FormatOpenPortLine literal) names its resolution path; the listen-port TOCTOU has a recorded fallback.
- **Type consistency:** `Cli.RunAsync(string[], Stream, Stream, TextWriter, CancellationToken)` consistent across skeleton/Main/tests; `RunCoreAsync` private signature consistent; helpers (`StartLoopbackListener`/`GetClosedPort`/`GetProbablyFreePort`/`ParseLastLine`) used only within their files.
- **Verified at planning:** SIGINT envelope + text-line-before-envelope shape probed (linux-x64); DNS-failure prefix probed (OS-specific suffix excluded from assertions); library already references ShellKit + InternalsVisibleTo confirmed; the ColorTests "no seam" comment located (authorised fixup); engines' stream-taking signatures confirmed from Program.cs call sites; pre-refactor count 124/0.
