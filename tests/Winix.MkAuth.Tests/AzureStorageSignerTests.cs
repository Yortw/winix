using System.Collections.Generic;
using Winix.MkAuth;
using Xunit;

public class AzureStorageSignerTests
{
    // Reference vector captured from Azure.Storage.Blobs 12.24.0's StorageSharedKeyCredential.
    // An independent, non-SDK HMAC-SHA256 oracle (hand-built StringToSign) was proven byte-for-byte
    // equal to the SDK's signature across multiple request shapes during the spike; this fixed-date
    // vector was then computed by that proven oracle. See task report / ADR §7.
    [Fact]
    public void Matches_azure_sdk_reference_signature()
    {
        var req = new AzureStorageRequest
        {
            Account = "devstoreaccount1",
            KeyBase64 = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==",
            Method = "GET",
            Url = "https://devstoreaccount1.blob.core.windows.net/mycontainer/myblob.txt?restype=container&comp=metadata",
            XmsDate = "Fri, 26 Jun 2026 00:00:00 GMT",
            XmsVersion = "2021-08-06",
            Headers = new Dictionary<string, string>
            {
                ["x-ms-meta-color"] = "blue",
                ["x-ms-meta-author"] = "troy",
            },
        };

        var r = AzureStorageSigner.Sign(req);

        Assert.Equal("Authorization", r.Header.HeaderName);
        Assert.Equal("SharedKey devstoreaccount1:7uWLmJv3ZsbG12YwXWErV4J+NAHuR3PPpjRRYKAoSoQ=", r.Header.HeaderValue);
    }
}
