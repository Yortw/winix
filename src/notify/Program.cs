using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Winix.Notify;
using Yort.ShellKit;

namespace Notify;

internal sealed class Program
{
    // Static HttpClient — recommended pattern; AOT-friendly; lifetime spans the (one-shot) process.
    private static readonly Lazy<HttpClient> SharedHttp = new(() =>
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        return client;
    });

    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        var parse = ArgParser.Parse(args);
        if (parse.IsHandled)
        {
            return parse.HandledExitCode;
        }
        if (!parse.Success)
        {
            Console.Error.WriteLine($"notify: {parse.Error}");
            Console.Error.WriteLine("Run 'notify --help' for usage.");
            return ExitCode.UsageError;
        }

        var opts = parse.Options!;

        try
        {
            var backends = BuildBackends(opts);
            if (backends.Count == 0)
            {
                // Defensive — ArgParser should already have caught this.
                Console.Error.WriteLine("notify: no backends configured");
                return ExitCode.UsageError;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            IReadOnlyList<BackendResult> results = Dispatcher.SendAsync(backends, opts.ToMessage(), cts.Token)
                .GetAwaiter().GetResult();

            // Per-backend stderr warnings for failures (regardless of strict mode).
            foreach (var r in results)
            {
                if (!r.Ok)
                {
                    Console.Error.WriteLine($"notify: warning: {r.BackendName}: {r.Error}");
                }
            }

            if (opts.Json)
            {
                Console.Out.WriteLine(Formatting.Json(opts, results));
            }

            return ResolveExitCode(opts.Strict, results);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"notify: error: {ex.Message}");
            return 1;
        }
    }

    private static List<IBackend> BuildBackends(NotifyOptions opts)
    {
        var list = new List<IBackend>();
        if (opts.DesktopEnabled)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
#pragma warning disable CA1416 // Validate platform compatibility — guarded by IsOSPlatform check above.
                list.Add(new WindowsToastBackend());
#pragma warning restore CA1416
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                list.Add(new MacOsAppleScriptBackend());
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                list.Add(new LinuxNotifySendBackend());
            }
            // Other Unixes — no desktop backend, ntfy still available if configured.
        }
        if (opts.NtfyEnabled && opts.NtfyTopic is not null)
        {
            list.Add(new NtfyBackend(SharedHttp.Value, opts.NtfyServer, opts.NtfyTopic, opts.NtfyToken));
        }
        return list;
    }

    private static int ResolveExitCode(bool strict, IReadOnlyList<BackendResult> results)
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
