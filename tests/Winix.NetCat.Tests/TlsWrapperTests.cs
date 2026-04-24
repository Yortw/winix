#nullable enable

using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Winix.NetCat;
using Xunit;

namespace Winix.NetCat.Tests;

public sealed class TlsWrapperTests
{
    [Fact]
    public async Task WrapAsync_InsecureModeWithSelfSignedServer_AuthenticatesAndExchangesBytes()
    {
        // Generate an in-memory self-signed cert for "localhost".
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        // Round-trip via PFX so SslStream's Windows path treats it as a usable server cert.
        // (The new X509CertificateLoader API replaces the obsolete byte-array constructor.)
        byte[] pfxBytes = cert.Export(X509ContentType.Pfx);
        using var serverCert = X509CertificateLoader.LoadPkcs12(pfxBytes, password: null);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Task serverTask = Task.Run(async () =>
        {
            using TcpClient peer = await listener.AcceptTcpClientAsync();
            using var ssl = new SslStream(peer.GetStream(), leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsServerAsync(serverCert, clientCertificateRequired: false,
                enabledSslProtocols: System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
                checkCertificateRevocation: false);
            var buf = new byte[1024];
            int n = await ssl.ReadAsync(buf);
            await ssl.WriteAsync(buf.AsMemory(0, n));
        });

        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(IPAddress.Loopback, port);

            SslStream wrapped = await TlsWrapper.WrapClientAsync(tcp.GetStream(), targetHost: "localhost", insecure: true, ct: CancellationToken.None);

            byte[] payload = Encoding.ASCII.GetBytes("tls-roundtrip");
            await wrapped.WriteAsync(payload);
            var rb = new byte[1024];
            int n = await wrapped.ReadAsync(rb);
            Assert.Equal(payload, rb.AsMemory(0, n).ToArray());

            await wrapped.DisposeAsync();
        }
        finally
        {
            listener.Stop();
            await serverTask.WaitAsync(System.TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>
    /// Pins the AuthenticationException arm in NetCatClient's TLS handshake split (round-1 C2).
    /// Reverting the catch-split would either escape the exception (stack-trace crash) or
    /// mis-label it as generic transport failure.
    /// </summary>
    [Fact]
    public async Task NetCatClient_SelfSignedWithoutInsecure_ReturnsExitOne_TlsFailed()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        byte[] pfxBytes = cert.Export(X509ContentType.Pfx);
        using var serverCert = X509CertificateLoader.LoadPkcs12(pfxBytes, password: null);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Task serverTask = Task.Run(async () =>
        {
            try
            {
                using TcpClient peer = await listener.AcceptTcpClientAsync();
                using var ssl = new SslStream(peer.GetStream(), leaveInnerStreamOpen: false);
                await ssl.AuthenticateAsServerAsync(serverCert, clientCertificateRequired: false,
                    enabledSslProtocols: System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
                    checkCertificateRevocation: false);
            }
            catch { /* client will reject cert — expected */ }
        });

        try
        {
            var options = new NetCatOptions
            {
                Mode = NetCatMode.Connect,
                Protocol = NetCatProtocol.Tcp,
                Host = "127.0.0.1",
                Ports = new[] { new Winix.NetCat.PortRange(port) },
                UseTls = true,
                InsecureTls = false,
                Timeout = System.TimeSpan.FromSeconds(5),
            };

            using var stdin = new MemoryStream();
            using var stdout = new MemoryStream();
            using var stderr = new StringWriter();

            RunResult result = await new NetCatClient().RunAsync(options, stdin, stdout, stderr, CancellationToken.None);

            Assert.Equal(1, result.ExitCode);
            Assert.Equal("tls_failed", result.ExitReason);
            string err = stderr.ToString();
            Assert.Contains("TLS", err);
        }
        finally
        {
            listener.Stop();
            try { await serverTask.WaitAsync(System.TimeSpan.FromSeconds(5)); } catch { }
        }
    }

    /// <summary>
    /// Pins round-2 C2 at the TLS seam: a TLS server that accepts the TCP connection but hangs
    /// during the handshake must time out at <c>--timeout N</c>. Before round-2 the handshake
    /// shared <c>connectCts</c> (already consumed by the TCP connect step), so the handshake
    /// had no deadline and <c>nc --tls -w 1 host 443</c> blocked indefinitely. Round-4 added
    /// the fresh <c>tlsCts</c> + <c>CancelAfter</c>; this test ensures reverting that change
    /// fails a named test rather than shipping silently.
    /// </summary>
    [Fact]
    public async Task NetCatClient_TlsHandshakeHangs_ReturnsExitTwo_Timeout()
    {
        // Listener accepts the TCP connection but never calls AuthenticateAsServerAsync — the
        // client-side handshake stalls waiting for ServerHello.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Task serverTask = Task.Run(async () =>
        {
            try
            {
                using TcpClient peer = await listener.AcceptTcpClientAsync();
                await Task.Delay(5000); // hold the connection open, never respond to ClientHello
            }
            catch { /* listener shutdown */ }
        });

        try
        {
            var options = new NetCatOptions
            {
                Mode = NetCatMode.Connect,
                Protocol = NetCatProtocol.Tcp,
                Host = "127.0.0.1",
                Ports = new[] { new Winix.NetCat.PortRange(port) },
                UseTls = true,
                InsecureTls = true,
                Timeout = System.TimeSpan.FromMilliseconds(400),
            };

            using var stdin = new MemoryStream();
            using var stdout = new MemoryStream();
            using var stderr = new StringWriter();

            RunResult result = await new NetCatClient().RunAsync(options, stdin, stdout, stderr, CancellationToken.None);

            Assert.Equal(2, result.ExitCode);
            Assert.Equal("timeout", result.ExitReason);
            Assert.Contains("TLS handshake timed out", stderr.ToString());
        }
        finally
        {
            listener.Stop();
            try { await serverTask.WaitAsync(System.TimeSpan.FromSeconds(10)); } catch { }
        }
    }

    /// <summary>
    /// Pins the TLS IOException catch arm — distinct from AuthenticationException (cert
    /// validation) but mapped to the same exit_reason <c>tls_failed</c>. A peer that accepts
    /// TCP then immediately RSTs during the handshake surfaces as IOException, not
    /// AuthenticationException. Reverting the IOException arm would let the exception escape
    /// to Main's 126 safety-net instead of cleanly exiting 1 / tls_failed.
    /// </summary>
    [Fact]
    public async Task NetCatClient_TlsHandshakeTransportRst_ReturnsExitOne_TlsFailed()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Task serverTask = Task.Run(async () =>
        {
            try
            {
                using TcpClient peer = await listener.AcceptTcpClientAsync();
                // Force a hard reset: disable linger with timeout=0, then close.
                peer.LingerState = new LingerOption(true, 0);
                peer.Close();
            }
            catch { /* shutdown */ }
        });

        try
        {
            var options = new NetCatOptions
            {
                Mode = NetCatMode.Connect,
                Protocol = NetCatProtocol.Tcp,
                Host = "127.0.0.1",
                Ports = new[] { new Winix.NetCat.PortRange(port) },
                UseTls = true,
                InsecureTls = true,
                Timeout = System.TimeSpan.FromSeconds(5),
            };

            using var stdin = new MemoryStream();
            using var stdout = new MemoryStream();
            using var stderr = new StringWriter();

            RunResult result = await new NetCatClient().RunAsync(options, stdin, stdout, stderr, CancellationToken.None);

            Assert.Equal(1, result.ExitCode);
            Assert.Equal("tls_failed", result.ExitReason);
            // Stderr should mention TLS — message shape is either "handshake I/O error" OR
            // "certificate validation failed" depending on whether the peer got far enough to
            // present anything. We don't pin the exact wording so the test remains robust across
            // .NET runtime SslStream behaviour variations.
            Assert.Contains("TLS", stderr.ToString());
        }
        finally
        {
            listener.Stop();
            try { await serverTask.WaitAsync(System.TimeSpan.FromSeconds(5)); } catch { }
        }
    }

    /// <summary>
    /// Pins the security-relevant stderr warning emitted when <c>--insecure --tls</c> is used.
    /// A regression that drops the warning would silently run with cert validation disabled —
    /// exactly the class of silent-failure flagged by round-5 test-analyzer I13.
    /// </summary>
    [Fact]
    public async Task NetCatClient_InsecureTls_EmitsStderrWarning()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        byte[] pfxBytes = cert.Export(X509ContentType.Pfx);
        using var serverCert = X509CertificateLoader.LoadPkcs12(pfxBytes, password: null);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Task serverTask = Task.Run(async () =>
        {
            using TcpClient peer = await listener.AcceptTcpClientAsync();
            using var ssl = new SslStream(peer.GetStream(), leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsServerAsync(serverCert, clientCertificateRequired: false,
                enabledSslProtocols: System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
                checkCertificateRevocation: false);
        });

        try
        {
            var options = new NetCatOptions
            {
                Mode = NetCatMode.Connect,
                Protocol = NetCatProtocol.Tcp,
                Host = "127.0.0.1",
                Ports = new[] { new Winix.NetCat.PortRange(port) },
                UseTls = true,
                InsecureTls = true,
                Timeout = System.TimeSpan.FromSeconds(5),
            };

            using var stdin = new MemoryStream();
            using var stdout = new MemoryStream();
            using var stderr = new StringWriter();

            await new NetCatClient().RunAsync(options, stdin, stdout, stderr, CancellationToken.None);

            Assert.Contains("certificate validation disabled", stderr.ToString());
        }
        finally
        {
            listener.Stop();
            try { await serverTask.WaitAsync(System.TimeSpan.FromSeconds(5)); } catch { }
        }
    }
}
