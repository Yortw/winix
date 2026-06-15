#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Winix.SecretStore;

// Bring each tool's namespace into scope with aliases to avoid the Winix.Winix project
// namespace shadowing the Winix.* tool namespaces when resolved from within this project.
using TimeItCli   = global::Winix.TimeIt.Cli;
using SqueezeCli  = global::Winix.Squeeze.Cli;
using PeepCli     = global::Winix.Peep.Cli;
using WargsCli    = global::Winix.Wargs.Cli;
using FilesCli    = global::Winix.Files.Cli;
using TreeXCli    = global::Winix.TreeX.Cli;
using ManCli      = global::Winix.Man.Cli;
using LessCli     = global::Winix.Less.Cli;
using WhoHoldsCli = global::Winix.WhoHolds.Cli;
using ScheduleCli = global::Winix.Schedule.Cli;
using NcCli       = global::Winix.NetCat.Cli;
using WinixCli    = global::Winix.Winix.Cli;
using RetryCli    = global::Winix.Retry.Cli;
using WhenCli     = global::Winix.When.Cli;
using ClipCli     = global::Winix.Clip.Cli;
using IdsCli      = global::Winix.Ids.Cli;
using DigestCli   = global::Winix.Digest.Cli;
using NotifyCli   = global::Winix.Notify.Cli;
using UrlCli      = global::Winix.Url.Cli;
using QrCli       = global::Winix.Qr.Cli;
using ProtectCli  = global::Winix.Protect.Cli;
using EnvVaultCli = global::Winix.EnvVault.Cli;
using MkSecretCli = global::Winix.MkSecret.Cli;
using MkAuthCli   = global::Winix.MkAuth.Cli;
using TrashCli    = global::Winix.Trash.Cli;
using HCatCli     = global::Winix.HCat.Cli;
using DemuxCli    = global::Winix.Demux.Cli;
using OnlineCli   = global::Winix.Online.Cli;
using RunForCli   = global::Winix.RunFor.Cli;
using IProcessLauncherAlias = global::Winix.EnvVault.IProcessLauncher;
using IConsolePromptAlias   = global::Winix.EnvVault.IConsolePrompt;

namespace Winix.Contract.Tests;

/// <summary>
/// Every --describe surface in the suite: key → async adapter invoking the library seam.
/// Keys: "tool" or "tool/subcommand". A new tool MUST be registered here and its
/// snapshot committed (CLAUDE.md new-tool checklist). Sync seams wrap in
/// Task.FromResult; async seams are awaited natively (never blocked).
/// </summary>
/// <remarks>
/// Seam signatures are copied verbatim from each tool's existing seam tests (not guessed).
/// Some seams have required non-IO deps. envvault: ISecretStore is the shared functional
/// Winix.SecretStore.NullSecretStore (in-memory, does NOT throw); IProcessLauncher and
/// IConsolePrompt are inlined throwing stubs below. notify: a fresh HttpClient. None of
/// these are exercised by --describe — ShellKit handles --describe during Parse() before
/// reaching any domain logic, so the throwing stubs are never hit and the store is never read.
/// </remarks>
internal static class DescribeSurfaces
{
    /// <summary>
    /// All registered --describe surfaces. Invoke each entry under
    /// <see cref="ConsoleCapture.RunAsync"/> to capture the JSON emitted to Console.Out.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Func<string[], Task<int>>> All =
        new Dictionary<string, Func<string[], Task<int>>>(StringComparer.Ordinal)
        {
            // ── timeit ────────────────────────────────────────────────────────────────
            // Signature from Winix.TimeIt.Tests/CliTests.cs: Cli.Run(args, stdout, stderr)
            ["timeit"] = args => Task.FromResult(
                TimeItCli.Run(args, TextWriter.Null, TextWriter.Null)),

            // ── squeeze ───────────────────────────────────────────────────────────────
            // Signature from Winix.Squeeze.Tests/CliTests.cs:
            //   Cli.RunAsync(args, stdin, stdout, stderr, stdinIsRedirected, stdoutIsTerminal)
            ["squeeze"] = args => SqueezeCli.RunAsync(
                args, Stream.Null, Stream.Null, TextWriter.Null,
                stdinIsRedirected: false,
                stdoutIsTerminal: false),

            // ── peep ──────────────────────────────────────────────────────────────────
            // Signature from Winix.Peep.Tests/CliRunAsyncTests.cs:
            //   Cli.RunAsync(args, stdout, stderr, token)
            ["peep"] = args => PeepCli.RunAsync(
                args, TextWriter.Null, TextWriter.Null, CancellationToken.None),

            // ── wargs ─────────────────────────────────────────────────────────────────
            // Signature from Winix.Wargs.Tests/CliRunAsyncTests.cs:
            //   Cli.RunAsync(args, stdin, stdout, stderr, CancellationToken.None)
            ["wargs"] = args => WargsCli.RunAsync(
                args, TextReader.Null, TextWriter.Null, TextWriter.Null, CancellationToken.None),

            // ── files ─────────────────────────────────────────────────────────────────
            // Signature from Winix.Files.Tests/CliRunTests.cs:
            //   Cli.Run(args, stdout, stderr, isStdoutRedirected)
            ["files"] = args => Task.FromResult(
                FilesCli.Run(args, TextWriter.Null, TextWriter.Null, isStdoutRedirected: true)),

            // ── treex ─────────────────────────────────────────────────────────────────
            // Signature from Winix.TreeX.Tests/CliRunTests.cs:
            //   Cli.Run(args, stdout, stderr, isStdoutRedirected)
            ["treex"] = args => Task.FromResult(
                TreeXCli.Run(args, TextWriter.Null, TextWriter.Null, isStdoutRedirected: true)),

            // ── man ───────────────────────────────────────────────────────────────────
            // Signature from Winix.Man.Tests/CliRunTests.cs:
            //   Cli.Run(args, stdout, stderr, isTerminal, terminalWidth, exeDirectory, manpathEnv)
            // exeDirectory is required (non-null); AppContext.BaseDirectory is the canonical
            // default the production Program.cs uses (resolves the exe's location).
            ["man"] = args => Task.FromResult(
                ManCli.Run(
                    args,
                    TextWriter.Null,
                    TextWriter.Null,
                    isTerminal: false,
                    terminalWidth: 80,
                    exeDirectory: AppContext.BaseDirectory)),

            // ── less ──────────────────────────────────────────────────────────────────
            // Signature from Winix.Less.Tests/CliRunTests.cs:
            //   Cli.Run(args, stdout, stderr, isStdoutRedirected, isStdinRedirected, pagerRunner)
            ["less"] = args => Task.FromResult(
                LessCli.Run(
                    args,
                    TextWriter.Null,
                    TextWriter.Null,
                    isStdoutRedirected: true,
                    isStdinRedirected: false)),

            // ── whoholds ──────────────────────────────────────────────────────────────
            // Signature from Winix.WhoHolds.Tests/ColorTests.cs:
            //   Cli.Run(args, stdout, stderr, isStdoutRedirected, portFinder, fileFinder, isElevated)
            // Optional finder/isElevated params default to production impls; fine for --describe.
            ["whoholds"] = args => Task.FromResult(
                WhoHoldsCli.Run(args, TextWriter.Null, TextWriter.Null, isStdoutRedirected: true)),

            // ── schedule ──────────────────────────────────────────────────────────────
            // Signature from Winix.Schedule.Tests/CliRunTests.cs:
            //   Cli.Run(args, stdout, stderr, backend)
            // The optional backend param defaults to null (uses production backend);
            // --describe is handled before any backend dispatch.
            ["schedule"] = args => Task.FromResult(
                ScheduleCli.Run(args, TextWriter.Null, TextWriter.Null)),

            // ── nc ────────────────────────────────────────────────────────────────────
            // Signature from Winix.NetCat.Tests/CliRunAsyncTests.cs:
            //   Cli.RunAsync(args, stdin, stdout, stderr, CancellationToken.None)
            // stdin/stdout are byte Streams (not TextWriter) — nc is byte-stream oriented.
            ["nc"] = args => NcCli.RunAsync(
                args, Stream.Null, Stream.Null, TextWriter.Null, CancellationToken.None),

            // ── winix ─────────────────────────────────────────────────────────────────
            // Signature from Winix.Winix.Tests/CliTests.cs:
            //   Cli.RunAsync(args, stdout, stderr, adapters, platform, manifestLoader) [all optional]
            ["winix"] = args => WinixCli.RunAsync(
                args, TextWriter.Null, TextWriter.Null),

            // ── retry ─────────────────────────────────────────────────────────────────
            // Signature from Winix.Retry.Tests/CliRunTests.cs:
            //   Cli.Run(args, stdout, stderr, CancellationToken.None)
            ["retry"] = args => Task.FromResult(
                RetryCli.Run(args, TextWriter.Null, TextWriter.Null, CancellationToken.None)),

            // ── when ──────────────────────────────────────────────────────────────────
            // Signature from Winix.When.Tests/CliTests.cs:
            //   Cli.Run(args, stdout, stderr)
            ["when"] = args => Task.FromResult(
                WhenCli.Run(args, TextWriter.Null, TextWriter.Null)),

            // ── clip ──────────────────────────────────────────────────────────────────
            // Signature from Winix.Clip.Tests/CliRunTests.cs (direct Cli.Run call):
            //   Cli.Run(args, payloadStdin, isStdinRedirected, stdout, stderr, backendFactory)
            // Optional backendFactory defaults to null (production factory); safe for --describe.
            ["clip"] = args => Task.FromResult(
                ClipCli.Run(
                    args,
                    Stream.Null,
                    isStdinRedirected: false,
                    stdout: TextWriter.Null,
                    stderr: TextWriter.Null)),

            // ── ids ───────────────────────────────────────────────────────────────────
            // Signature from Winix.Ids.Tests/CliTests.cs:
            //   Cli.Run(args, stdout, stderr, generatorOverride) [generatorOverride optional]
            ["ids"] = args => Task.FromResult(
                IdsCli.Run(args, TextWriter.Null, TextWriter.Null)),

            // ── digest ────────────────────────────────────────────────────────────────
            // Signature from Winix.Digest.Tests/CliTests.cs:
            //   Cli.Run(args, keyStdin, payloadStdin, stdout, stderr)
            ["digest"] = args => Task.FromResult(
                DigestCli.Run(
                    args: args,
                    keyStdin: TextReader.Null,
                    payloadStdin: Stream.Null,
                    stdout: TextWriter.Null,
                    stderr: TextWriter.Null)),

            // ── notify ────────────────────────────────────────────────────────────────
            // Signature from src/Winix.Notify/Cli.cs:
            //   Cli.Run(args, http, stdout, stderr)
            // HttpClient is required. A new instance is safe here — --describe exits
            // before any HTTP call is made.
            ["notify"] = args => Task.FromResult(
                NotifyCli.Run(args, new HttpClient(), TextWriter.Null, TextWriter.Null)),

            // ── url ───────────────────────────────────────────────────────────────────
            // Signature from Winix.Url.Tests/CliTests.cs:
            //   Cli.Run(args, stdout, stderr)
            ["url"] = args => Task.FromResult(
                UrlCli.Run(args, TextWriter.Null, TextWriter.Null)),

            // ── qr ────────────────────────────────────────────────────────────────────
            // Signature from Winix.Qr.Tests/CliTests.cs:
            //   Cli.Run(args, reader, outW, errW, binW, stdinRedirected, stdoutIsTty)
            ["qr"] = args => Task.FromResult(
                QrCli.Run(
                    args,
                    TextReader.Null,
                    TextWriter.Null,
                    TextWriter.Null,
                    Stream.Null,
                    stdinIsRedirected: false,
                    stdoutIsTty: false)),

            // ── protect ───────────────────────────────────────────────────────────────
            // Signature from Winix.Protect.Tests/CliErrorHandlingTests.cs:
            //   Winix.Protect.Cli.Run(args, invocationName)
            // protect and unprotect share one library with invocationName distinguishing them.
            ["protect"] = args => Task.FromResult(
                ProtectCli.Run(args, "protect")),

            // ── unprotect ─────────────────────────────────────────────────────────────
            // Same library as protect, different invocation name.
            ["unprotect"] = args => Task.FromResult(
                ProtectCli.Run(args, "unprotect")),

            // ── envvault ──────────────────────────────────────────────────────────────
            // Signature from Winix.EnvVault.Tests/CliTests.cs:
            //   Cli.Run(args, store, launcher, prompt, stdout, stderr, stdoutIsTty)
            // store: the shared Winix.SecretStore.NullSecretStore (functional in-memory, does
            // NOT throw). launcher/prompt: inlined throwing stubs below. None are reached for
            // --describe — ShellKit handles it during Parse(), before any domain call.
            ["envvault"] = args => Task.FromResult(
                EnvVaultCli.Run(
                    args,
                    store: new NullSecretStore(),
                    launcher: new NullProcessLauncher(),
                    prompt: new NullConsolePrompt(),
                    stdout: TextWriter.Null,
                    stderr: TextWriter.Null)),

            // ── mksecret ──────────────────────────────────────────────────────────────
            // Signature from Winix.MkSecret.Tests/CliTests.cs:
            //   Cli.Run(args, stdout, stderr, rng) [rng optional]
            ["mksecret"] = args => Task.FromResult(
                MkSecretCli.Run(args, TextWriter.Null, TextWriter.Null)),

            // ── mkauth ────────────────────────────────────────────────────────────────
            // Signature from Winix.MkAuth.Tests/CliTests.cs:
            //   Cli.Run(args, stdout, stderr, stdin, deps) [deps optional]
            ["mkauth"] = args => Task.FromResult(
                MkAuthCli.Run(args, TextWriter.Null, TextWriter.Null, TextReader.Null)),

            // ── trash ─────────────────────────────────────────────────────────────────
            // Signature from Winix.Trash.Tests/CliTests.cs:
            //   Cli.Run(args, stdout, stderr, backendOverride, isInteractiveOverride, readLineOverride)
            // All override params are optional (default null = production impls).
            ["trash"] = args => Task.FromResult(
                TrashCli.Run(args, TextWriter.Null, TextWriter.Null)),

            // ── hcat ──────────────────────────────────────────────────────────────────
            // Signature from Winix.HCat.Tests/CliTests.cs:
            //   Cli.Run(args, stdout, stderr)
            ["hcat"] = args => Task.FromResult(
                HCatCli.Run(args, TextWriter.Null, TextWriter.Null)),

            // ── demux ─────────────────────────────────────────────────────────────────
            // Signature from Winix.Demux.Tests/CliTests.cs:
            //   Cli.Run(args, stdin, stdout, stderr)
            ["demux"] = args => Task.FromResult(
                DemuxCli.Run(args, TextReader.Null, TextWriter.Null, TextWriter.Null)),

            // ── online ────────────────────────────────────────────────────────────────
            // Signature from Winix.Online.Tests/CliRunAsyncTests.cs:
            //   Cli.RunAsync(args, stdout, stderr, CancellationToken.None)  [public overload]
            ["online"] = args => OnlineCli.RunAsync(
                args, TextWriter.Null, TextWriter.Null, CancellationToken.None),

            // ── runfor ────────────────────────────────────────────────────────────────
            // Signature from Winix.RunFor.Tests/CliRunTests.cs:
            //   Cli.Run(args, stdout, stderr, CancellationToken.None, starter?)  [starter optional]
            ["runfor"] = args => Task.FromResult(
                RunForCli.Run(args, TextWriter.Null, TextWriter.Null, CancellationToken.None)),

            // ── qr subcommands ────────────────────────────────────────────────────────
            // Each helper subcommand emits its own distinct envelope (tool: "qr wifi", etc.).
            // Args are supplied by the snapshot test as [subcommand, "--describe"].
            // Exact QrCli.Run signature copied from the "qr" entry above.
            ["qr/wifi"] = args => Task.FromResult(
                QrCli.Run(
                    args,
                    TextReader.Null,
                    TextWriter.Null,
                    TextWriter.Null,
                    Stream.Null,
                    stdinIsRedirected: false,
                    stdoutIsTty: false)),

            ["qr/sms"] = args => Task.FromResult(
                QrCli.Run(
                    args,
                    TextReader.Null,
                    TextWriter.Null,
                    TextWriter.Null,
                    Stream.Null,
                    stdinIsRedirected: false,
                    stdoutIsTty: false)),

            ["qr/mailto"] = args => Task.FromResult(
                QrCli.Run(
                    args,
                    TextReader.Null,
                    TextWriter.Null,
                    TextWriter.Null,
                    Stream.Null,
                    stdinIsRedirected: false,
                    stdoutIsTty: false)),

            ["qr/geo"] = args => Task.FromResult(
                QrCli.Run(
                    args,
                    TextReader.Null,
                    TextWriter.Null,
                    TextWriter.Null,
                    Stream.Null,
                    stdinIsRedirected: false,
                    stdoutIsTty: false)),

            ["qr/tel"] = args => Task.FromResult(
                QrCli.Run(
                    args,
                    TextReader.Null,
                    TextWriter.Null,
                    TextWriter.Null,
                    Stream.Null,
                    stdinIsRedirected: false,
                    stdoutIsTty: false)),

            // ── mksecret subcommands ──────────────────────────────────────────────────
            // mksecret/phrase and mksecret/key emit distinct envelopes (tool: "mksecret phrase"
            // / "mksecret key", maturity "fresh"). Exact MkSecretCli.Run signature copied
            // from the "mksecret" entry above.
            ["mksecret/phrase"] = args => Task.FromResult(
                MkSecretCli.Run(args, TextWriter.Null, TextWriter.Null)),

            ["mksecret/key"] = args => Task.FromResult(
                MkSecretCli.Run(args, TextWriter.Null, TextWriter.Null)),
        };

    // ── Minimal stubs for envvault's required non-IO deps ────────────────────────────
    // These implement only the interfaces; every member throws NotSupportedException because
    // none will ever be called when --describe is passed (ShellKit exits during Parse()).

    /// <summary>
    /// Stub <see cref="IProcessLauncherAlias"/> for the envvault --describe seam call.
    /// Not called at runtime; envvault's parser handles --describe before reaching exec.
    /// </summary>
    private sealed class NullProcessLauncher : IProcessLauncherAlias
    {
        /// <inheritdoc/>
        public int Launch(
            string fileName,
            System.Collections.Generic.IReadOnlyList<string> argv,
            System.Collections.Generic.IReadOnlyDictionary<string, string> extraEnv)
            => throw new NotSupportedException("NullProcessLauncher is a --describe stub only.");
    }

    /// <summary>
    /// Stub <see cref="IConsolePromptAlias"/> for the envvault --describe seam call.
    /// Not called at runtime; envvault's parser handles --describe before reaching any prompt.
    /// </summary>
    private sealed class NullConsolePrompt : IConsolePromptAlias
    {
        /// <inheritdoc/>
        public bool IsInteractive => false;

        /// <inheritdoc/>
        public void WritePrompt(string text)
            => throw new NotSupportedException("NullConsolePrompt is a --describe stub only.");

        /// <inheritdoc/>
        public string ReadLineEchoOff()
            => throw new NotSupportedException("NullConsolePrompt is a --describe stub only.");

        /// <inheritdoc/>
        public string? ReadLineFromStdin()
            => throw new NotSupportedException("NullConsolePrompt is a --describe stub only.");
    }
}
