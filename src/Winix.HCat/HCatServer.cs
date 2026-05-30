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

        switch (options.Mode)
        {
            case HCatMode.Serve:
                ServeConfig.Apply(app, options);
                break;
            case HCatMode.Inspect:
                // Task 16 wires onRecord to JSONL-stdout + the CaptureController; no-op for now.
                InspectHandler.Apply(app, options, static _ => { });
                break;
            case HCatMode.Pipe:
                // Task 16 wires onRecord to JSONL-stdout + the CaptureController; no-op for now.
                PipeHandler.Apply(app, options, static _ => { });
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
    internal static async Task RunForTestAsync(
        HCatOptions options,
        TaskCompletionSource<string> ready,
        CancellationToken ct)
    {
        WebApplication? app = null;
        try
        {
            // Ephemeral loopback: let the OS pick the port (0), then read it back. When Https is set the
            // endpoint is configured with the self-signed cert inside BuildApp, so the bound address Kestrel
            // reports back already carries the https:// scheme.
            app = BuildApp(options, IPAddress.Loopback, port: 0);

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
            return;
        }

        try
        {
            // Block until cancellation, then shut down gracefully.
            var cancelled = new TaskCompletionSource();
            using (ct.Register(() => cancelled.TrySetResult()))
            {
                await cancelled.Task.ConfigureAwait(false);
            }
            await app.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await app.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Production entry point: resolves the bind target via <see cref="BindResolver"/>, configures
    /// Kestrel, renders the start banner to <paramref name="banner"/>, runs until cancellation, and maps the
    /// outcome to a POSIX-style exit code. CI stop conditions and richer exit-code mapping are layered on in a
    /// later task; a clean run currently returns 0.</summary>
    /// <param name="options">The parsed invocation options.</param>
    /// <param name="banner">Writer for the human-readable start banner (typically stderr).</param>
    /// <param name="ct">Cancels the run; triggers graceful shutdown.</param>
    /// <returns>0 on a clean shutdown.</returns>
    public static async Task<int> RunAsync(HCatOptions options, TextWriter banner, CancellationToken ct)
    {
        BindInfo bind = BindResolver.Resolve(options, EnumerateLanIPv4);

        // Bind Kestrel directly to the resolved address/port; UseHttps (inside BuildApp) supplies the
        // self-signed cert when --https is set.
        var app = BuildApp(options, bind.Address, options.Port);

        // No QR yet (rendered in a later task); pass null so the banner is still informative.
        banner.Write(Banner.Render(bind, options, qr: null));
        banner.Flush();

        await app.StartAsync(ct).ConfigureAwait(false);
        try
        {
            var cancelled = new TaskCompletionSource();
            using (ct.Register(() => cancelled.TrySetResult()))
            {
                await cancelled.Task.ConfigureAwait(false);
            }
            await app.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await app.DisposeAsync().ConfigureAwait(false);
        }

        return 0;
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
