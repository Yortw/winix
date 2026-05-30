#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Winix.HCat.Handlers;
using Yort.ShellKit;

namespace Winix.HCat;

/// <summary>Builds and runs the hcat HTTP server. The build step (<see cref="BuildApp"/>) is separated from
/// the run step so integration tests can start on an ephemeral loopback port and read the bound address back.
/// Per-mode middleware is dispatched from <see cref="BuildApp"/>.</summary>
public static class HCatServer
{
    /// <summary>Builds the configured <see cref="WebApplication"/> for the given options and binds Kestrel to
    /// <paramref name="bindAddress"/>:<paramref name="port"/>. Configures AOT-friendly JSON via
    /// <see cref="HCatJsonContext"/> and dispatches per-mode middleware. When <see cref="HCatOptions.Https"/>
    /// is set, the endpoint uses TLS with a fresh in-memory self-signed certificate.</summary>
    /// <param name="options">The invocation options (mode, https, served directory, etc.).</param>
    /// <param name="bindAddress">The IP to listen on.</param>
    /// <param name="port">The port to listen on; 0 lets the OS pick an ephemeral port.</param>
    /// <remarks>HTTPS is wired via <c>listenOptions.UseHttps(cert)</c> rather than an <c>https://</c> URL so the
    /// self-signed certificate can be supplied explicitly. The certificate is created once and disposed on
    /// application shutdown so it lives as long as the listener references it.</remarks>
    internal static WebApplication BuildApp(HCatOptions options, IPAddress bindAddress, int port)
        => BuildApp(options, bindAddress, port, lifecycle: null);

    /// <summary>Builds the app and wires the inspect/pipe record sink to <paramref name="lifecycle"/> (JSONL
    /// output + CI stop condition). When <paramref name="lifecycle"/> is non-null, a middleware step runs AFTER
    /// the terminal handler returns and, if the stop condition was satisfied by that request, triggers a
    /// graceful shutdown — letting the triggering response flush first (F6).</summary>
    /// <param name="options">The invocation options.</param>
    /// <param name="bindAddress">The IP to listen on.</param>
    /// <param name="port">The port to listen on; 0 lets the OS pick an ephemeral port.</param>
    /// <param name="lifecycle">CI lifecycle coordinator, or null for serve mode / no CI conditions.</param>
    internal static WebApplication BuildApp(
        HCatOptions options, IPAddress bindAddress, int port, CaptureLifecycle? lifecycle)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();

        // AOT: register the source-gen context so any JSON System.Text.Json work avoids reflection.
        builder.Services.Configure<JsonOptions>(o =>
        {
            o.SerializerOptions.TypeInfoResolverChain.Insert(0, HCatJsonContext.Default);
        });

        // Cert lifetime: created once here and held by the UseHttps closure for the listener's lifetime;
        // disposed on ApplicationStopped (below) so it is not collected while the endpoint still references it.
        X509Certificate2? cert = options.Https ? SelfSignedCert.Create() : null;

        builder.Services.Configure<KestrelServerOptions>(kestrel =>
        {
            kestrel.Listen(bindAddress, port, listenOptions =>
            {
                if (cert is not null)
                {
                    listenOptions.UseHttps(cert);
                }
            });
        });

        var app = builder.Build();

        if (cert is not null)
        {
            app.Lifetime.ApplicationStopped.Register(cert.Dispose);
        }

        // F6: this middleware runs AFTER the terminal handler returns (the response is already flushed),
        // so triggering StopApplication here never truncates the request that satisfied the stop condition.
        // StopApplication() lets remaining in-flight requests drain by default.
        if (lifecycle is not null && options.Mode != HCatMode.Serve)
        {
            app.Use(async (context, next) =>
            {
                await next(context).ConfigureAwait(false);
                if (lifecycle.StopRequested)
                {
                    app.Lifetime.StopApplication();
                }
            });
        }

        Action<RequestRecord> onRecord = lifecycle is not null
            ? lifecycle.OnRecord
            : static _ => { };

        switch (options.Mode)
        {
            case HCatMode.Serve:
                ServeConfig.Apply(app, options);
                break;
            case HCatMode.Inspect:
                InspectHandler.Apply(app, options, onRecord);
                break;
            case HCatMode.Pipe:
                PipeHandler.Apply(app, options, onRecord);
                break;
        }

        return app;
    }

    /// <summary>Test entry point: builds the app, binds an ephemeral loopback port, starts the server, then
    /// resolves <paramref name="ready"/> with the actual bound base URL read from
    /// <see cref="IServerAddressesFeature"/>, and awaits cancellation before shutting down gracefully.</summary>
    /// <param name="options">The invocation options (mode + served directory).</param>
    /// <param name="ready">Completed with the bound base URL once the address is readable; faulted with the
    /// real exception if startup fails. Never left pending on a startup fault.</param>
    /// <param name="ct">Cancels the run; triggers graceful shutdown.</param>
    /// <remarks>F5: startup is wrapped so a bind failure faults <paramref name="ready"/> with the real cause
    /// (failing the awaiting test fast) instead of hanging until the caller's <c>WaitAsync</c> timeout.
    /// <paramref name="ready"/> is resolved with a result ONLY after the bound address is successfully read.</remarks>
    internal static Task RunForTestAsync(
        HCatOptions options,
        TaskCompletionSource<string> ready,
        CancellationToken ct)
        => RunForTestAsync(options, ready, jsonSink: null, ct);

    /// <summary>Test entry point with a JSONL sink and CI lifecycle. Builds the app on an ephemeral loopback
    /// port, resolves <paramref name="ready"/> with the bound base URL, then runs until either a CI stop
    /// condition fires, the <c>--timeout</c> elapses, or <paramref name="ct"/> cancels. Returns the outcome
    /// exit code (0 = stop satisfied / clean cancel; 1 = timeout unmet).</summary>
    /// <param name="options">The invocation options (mode + CI conditions).</param>
    /// <param name="ready">Completed with the bound base URL once readable; faulted on a startup fault.</param>
    /// <param name="jsonSink">Receives one JSONL line per captured request (inspect/pipe), or null.</param>
    /// <param name="ct">Cancels the run; triggers graceful shutdown.</param>
    /// <returns>The outcome exit code per <see cref="CaptureLifecycle.ExitCode"/>.</returns>
    internal static async Task<int> RunForTestAsync(
        HCatOptions options,
        TaskCompletionSource<string> ready,
        TextWriter? jsonSink,
        CancellationToken ct)
    {
        var controller = new CaptureController(options.CaptureCount, options.ExitOn);
        using var lifecycle = new CaptureLifecycle(controller, jsonSink);

        WebApplication? app = null;
        try
        {
            // Ephemeral loopback: let the OS pick the port (0), then read it back. When Https is set the
            // endpoint is configured with the self-signed cert inside BuildApp, so the bound address Kestrel
            // reports back already carries the https:// scheme.
            app = BuildApp(options, IPAddress.Loopback, port: 0, lifecycle);

            await app.StartAsync(ct).ConfigureAwait(false);

            var addresses = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>();
            string? bound = addresses?.Addresses.FirstOrDefault();
            if (bound is null)
            {
                throw new InvalidOperationException(
                    "Server started but no bound address was available from IServerAddressesFeature.");
            }

            // Resolve readiness ONLY after the address is readable (F5).
            ready.TrySetResult(bound);
        }
        catch (Exception ex)
        {
            // F5: surface the real cause so the awaiting test fails fast instead of hanging.
            ready.TrySetException(ex);
            if (app is not null)
            {
                try
                {
                    await app.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort cleanup; the original startup fault is what matters.
                }
            }
            return ExitCode.NotExecutable;
        }

        try
        {
            await AwaitStopRequestAsync(app, lifecycle, options.Timeout, ct).ConfigureAwait(false);
            await app.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await app.DisposeAsync().ConfigureAwait(false);
        }

        return lifecycle.ExitCode();
    }

    /// <summary>Blocks until a shutdown is requested by ANY of: a CI stop condition satisfied (middleware →
    /// <see cref="IHostApplicationLifetime.StopApplication"/>), the <c>--timeout</c> elapsing, or
    /// <paramref name="ct"/> cancelling. Resolves when <see cref="IHostApplicationLifetime.ApplicationStopping"/>
    /// fires.</summary>
    /// <remarks>We wait on <c>ApplicationStopping</c>, NOT <c>ApplicationStopped</c>: for a manually-started host
    /// (<c>StartAsync</c> without <c>RunAsync</c>), <c>StopApplication()</c> only fires the <c>Stopping</c>
    /// token — <c>Stopped</c> fires later, from the explicit <c>StopAsync()</c> the caller makes after this
    /// returns. Waiting on <c>Stopped</c> here would deadlock (it never fires before <c>StopAsync</c>).</remarks>
    private static async Task AwaitStopRequestAsync(
        WebApplication app, CaptureLifecycle lifecycle, TimeSpan? timeout, CancellationToken ct)
    {
        IHostApplicationLifetime applifetime = app.Lifetime;

        // F9: the timeout latches outcome 1 (if no request satisfied the condition first) then stops the host.
        lifecycle.StartTimeout(timeout, () => applifetime.StopApplication());

        var stopping = new TaskCompletionSource();
        using (applifetime.ApplicationStopping.Register(() => stopping.TrySetResult()))
        using (ct.Register(() => applifetime.StopApplication()))
        {
            await stopping.Task.ConfigureAwait(false);
        }
    }

    /// <summary>Production entry point: resolves the bind target via <see cref="BindResolver"/>, configures
    /// Kestrel, renders the start banner to <paramref name="banner"/>, runs until a CI stop condition / timeout
    /// / Ctrl+C, and maps the outcome to a POSIX-style exit code.</summary>
    /// <param name="options">The parsed invocation options.</param>
    /// <param name="banner">Writer for the human-readable start banner (typically stderr). Startup-failure
    /// messages (bind/cert) are written here too.</param>
    /// <param name="ct">Cancels the run; triggers graceful shutdown.</param>
    /// <returns>0 on a clean shutdown or a satisfied CI stop condition; 1 when <c>--timeout</c> elapsed without
    /// the stop condition being met; 126 when the listener could not bind or the TLS certificate failed.</returns>
    /// <remarks>F7/F10: a Kestrel bind failure (address-in-use) or a TLS-cert init failure is caught here and
    /// mapped to a FIXED English message and exit 126. The framework's <c>ex.Message</c> is NOT surfaced for
    /// these known faults — under <c>InvariantGlobalization=true</c> it returns SR keys, not English.</remarks>
    public static async Task<int> RunAsync(HCatOptions options, TextWriter banner, CancellationToken ct)
    {
        BindInfo bind = BindResolver.Resolve(options, EnumerateLanIPv4);

        var controller = new CaptureController(options.CaptureCount, options.ExitOn);
        using var lifecycle = new CaptureLifecycle(controller, options.Json ? Console.Out : null);

        WebApplication app;
        try
        {
            // Bind Kestrel directly to the resolved address/port; UseHttps (inside BuildApp) supplies the
            // self-signed cert when --https is set. Cert creation failures surface from BuildApp.
            app = BuildApp(options, bind.Address, options.Port, lifecycle);
        }
        catch (Exception ex) when (IsCertFailure(ex))
        {
            // F10: fixed English string — never pipe the framework cert-exception message to the user.
            banner.WriteLine("hcat: failed to initialise the TLS certificate");
            banner.Flush();
            return ExitCode.NotExecutable;
        }

        // No QR yet (rendered in a later task); pass null so the banner is still informative.
        banner.Write(Banner.Render(bind, options, qr: null));
        banner.Flush();

        try
        {
            await app.StartAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsAddressInUse(ex))
        {
            // F7/F10: address-in-use → fixed English string + 126. Do NOT surface ex.Message (SR key under AOT).
            banner.WriteLine($"hcat: cannot bind {bind.Address}:{options.Port} (address already in use)");
            banner.Flush();
            await app.DisposeAsync().ConfigureAwait(false);
            return ExitCode.NotExecutable;
        }
        catch
        {
            await app.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        try
        {
            await AwaitStopRequestAsync(app, lifecycle, options.Timeout, ct).ConfigureAwait(false);
            await app.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await app.DisposeAsync().ConfigureAwait(false);
        }

        return lifecycle.ExitCode();
    }

    /// <summary>True when the exception (or any inner) is the Kestrel "address already in use" bind failure.
    /// Kestrel wraps the OS error in an <see cref="IOException"/> whose inner is a <see cref="SocketException"/>
    /// with <see cref="SocketError.AddressAlreadyInUse"/>; both shapes are checked.</summary>
    private static bool IsAddressInUse(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is SocketException se && se.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>True when the exception originates from self-signed certificate creation
    /// (<see cref="SelfSignedCert"/>). Cert failures throw cryptographic exceptions; map them to 126.</summary>
    private static bool IsCertFailure(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is System.Security.Cryptography.CryptographicException)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Enumerates the machine's operational, non-loopback IPv4 addresses for LAN display URLs.</summary>
    private static IReadOnlyList<string> EnumerateLanIPv4()
    {
        var result = new List<string>();
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }
            foreach (UnicastIPAddressInformation ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(ua.Address))
                {
                    result.Add(ua.Address.ToString());
                }
            }
        }
        return result;
    }
}
