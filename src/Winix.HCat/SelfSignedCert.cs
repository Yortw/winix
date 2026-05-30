#nullable enable
using System;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Winix.HCat;

/// <summary>Generates an ephemeral self-signed certificate for <c>--https</c>. In-memory only — no
/// files written, no trust-store changes. The client must accept the warning (fine for dev/LAN).</summary>
public static class SelfSignedCert
{
    /// <summary>Creates a fresh self-signed RSA certificate valid for "localhost" + loopback, good for
    /// one year, usable directly as a Kestrel server certificate. The caller owns the returned certificate
    /// and should dispose it.</summary>
    /// <remarks>The certificate is round-tripped through an in-memory PKCS#12 blob. <c>CreateSelfSigned</c>
    /// produces a cert backed by an ephemeral CNG key that Windows SChannel cannot use for a TLS server
    /// endpoint (the handshake fails with an unexpected-EOF). Exporting to PFX and re-importing reassociates
    /// the private key in a form the platform TLS stack accepts. No files are written and no trust-store
    /// changes are made.</remarks>
    public static X509Certificate2 Create()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=hcat localhost", rsa, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        san.AddIpAddress(IPAddress.IPv6Loopback);
        req.CertificateExtensions.Add(san.Build());

        DateTimeOffset now = DateTimeOffset.UtcNow;
        using X509Certificate2 ephemeral = req.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(1));

        // SChannel-usable round-trip (see remarks). Empty password — the blob never leaves memory.
        byte[] pfx = ephemeral.Export(X509ContentType.Pfx);
        return X509CertificateLoader.LoadPkcs12(pfx, password: null);
    }
}
