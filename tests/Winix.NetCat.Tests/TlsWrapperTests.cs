#nullable enable

using System;
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
}
