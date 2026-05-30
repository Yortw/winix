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
    /// one year. The caller owns the returned certificate and should dispose it.</summary>
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
        return req.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(1));
    }
}
