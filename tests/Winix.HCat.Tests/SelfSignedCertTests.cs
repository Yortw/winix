using System;
using Winix.HCat;
using Xunit;

namespace Winix.HCat.Tests;

public class SelfSignedCertTests
{
    [Fact]
    public void Generates_a_usable_cert_with_private_key()
    {
        using var cert = SelfSignedCert.Create();
        Assert.True(cert.HasPrivateKey);
        Assert.True(cert.NotAfter > DateTime.Now);
    }
}
