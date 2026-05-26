#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Yort.ShellKit;

namespace Winix.Notify;

/// <summary>
/// Library-level entry point for the notify CLI. Program.cs is a thin shim around
/// <see cref="Run"/>; all behaviour lives here so it can be exercised by unit tests.
/// </summary>
/// <remarks>
/// Round-1 review TA-C1/C2 — pin the user-visible contract (exit codes per CI/cron use,
/// platform-conditional backend selection) without spawning processes. Extracting
/// <see cref="ResolveExitCode"/> and <see cref="BuildBackends"/> into the library, with
/// the OS branch parameterised, lets tests cover all four exit-code cells and each
/// platform branch deterministically.
/// </remarks>
public static class Cli
{
    /// <summary>
    /// Runs the notify pipeline: parse args, build backends, dispatch, format, return exit code.
    /// </summary>
    public static int Run(string[] args, HttpClient http, TextWriter stdout, TextWriter stderr)
    {
        var parse = ArgParser.Parse(args);
        if (parse.IsHandled) return parse.HandledExitCode;
        if (!parse.Success)
        {
            stderr.WriteLine($"notify: {parse.Error}");
            stderr.WriteLine("Run 'notify --help' for usage.");
            return ExitCode.UsageError;
        }

        var opts = parse.Options!;

        try
        {
            var backends = BuildBackends(opts, CurrentOSPlatform(), http);
            if (backends.Count == 0)
            {
                // Defensive — ArgParser should already have caught this.
                stderr.WriteLine("notify: no backends configured");
                return ExitCode.UsageError;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            IReadOnlyList<BackendResult> results = Dispatcher.SendAsync(backends, opts.ToMessage(), cts.Token)
                .GetAwaiter().GetResult();

            foreach (var r in results)
            {
                if (!r.Ok)
                {
                    stderr.WriteLine($"notify: warning: {r.BackendName}: {r.Error}");
                }
            }

            if (opts.Json)
            {
                stdout.WriteLine(Formatting.Json(opts, results));
            }

            return ResolveExitCode(opts.Strict, results);
        }
        catch (Exception ex)
        {
            // Round-1 review — generic catch returns ExitCode.NotExecutable (125), not 1.
            // Exit 1 is the documented strict-mode-failure code; a runtime crash returning 1
            // would be indistinguishable from "ntfy POST failed" for `notify ... || alert`
            // scripts. Anything unexpected here is a runtime fault → 125.
            stderr.WriteLine($"notify: error: {ex.GetType().Name}: {ex.Message}");
            return ExitCode.NotExecutable;
        }
    }

    /// <summary>
    /// Builds the list of backends that match the user's options for the given OS platform.
    /// Exposed for test pinning of the per-platform selection branches; production callers
    /// should pass <see cref="CurrentOSPlatform"/>.
    /// </summary>
    public static List<IBackend> BuildBackends(NotifyOptions opts, OSPlatform os, HttpClient http)
    {
        var list = new List<IBackend>();
        if (opts.DesktopEnabled)
        {
            if (os == OSPlatform.Windows)
            {
                // CA1416 false-positive: BuildWindowsToast wraps the platform-only constructor
                // and we've already gated on OSPlatform.Windows here. The runtime-OS gate is in
                // CurrentOSPlatform() / the test-injected `os` param, which is exactly what
                // CA1416 wants. Suppression is local and narrowly scoped.
#pragma warning disable CA1416
                list.Add(BuildWindowsToast());
#pragma warning restore CA1416
            }
            else if (os == OSPlatform.OSX)
            {
                list.Add(new MacOsAppleScriptBackend());
            }
            else if (os == OSPlatform.Linux)
            {
                list.Add(new LinuxNotifySendBackend());
            }
            // Other Unixes — no desktop backend, ntfy still available if configured.
        }
        if (opts.NtfyEnabled && opts.NtfyTopic is not null)
        {
            list.Add(new NtfyBackend(http, opts.NtfyServer, opts.NtfyTopic, opts.NtfyToken));
        }
        return list;
    }

    [SupportedOSPlatform("windows")]
    private static IBackend BuildWindowsToast() => new WindowsToastBackend();

    /// <summary>Returns the OSPlatform constant matching the running process. Used by Run() and tests pin Win/OSX/Linux explicitly.</summary>
    public static OSPlatform CurrentOSPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return OSPlatform.Windows;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return OSPlatform.OSX;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return OSPlatform.Linux;
        // Defaulting to Linux for "other Unix" matches the previous Program.cs fall-through:
        // no desktop backend gets added, ntfy still applies.
        return OSPlatform.Create("OTHER");
    }

    /// <summary>Maps a result vector to an exit code. Public so tests can lock the cells.</summary>
    public static int ResolveExitCode(bool strict, IReadOnlyList<BackendResult> results)
    {
        bool anyOk = false;
        bool anyFail = false;
        foreach (var r in results)
        {
            if (r.Ok) anyOk = true;
            else anyFail = true;
        }
        if (strict && anyFail) return 1;
        if (!anyOk) return ExitCode.NotExecutable;
        return ExitCode.Success;
    }
}
