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

    // A5 (TA-I3): second SDK-captured vector exercising previously-unexercised StringToSign slots —
    // a non-empty Content-Length (PUT with a body), a Content-Type, and an If-Match conditional —
    // plus several x-ms-* headers in the canonicalized block. The request shape AND the expected
    // signature below were captured together from one run of Azure.Storage.Blobs 12.24.0's
    // StorageSharedKeyCredential signing this exact PUT (fake account+key, no network — SharedKey
    // signing is local HMAC). The x-ms-date / x-ms-client-request-id are the SDK's per-run values,
    // frozen verbatim alongside the signature they produced. Our signer must reproduce that signature
    // byte-for-byte; the literal is the SDK's, not ours. (Harness: tmp/azvec/.)
    [Fact]
    public void Matches_azure_sdk_put_vector_with_content_and_conditional()
    {
        var req = new AzureStorageRequest
        {
            Account = "devstoreaccount1",
            KeyBase64 = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==",
            Method = "PUT",
            Url = "https://devstoreaccount1.blob.core.windows.net/mycontainer/myblob.txt?comp=metadata",
            XmsDate = "Sun, 07 Jun 2026 01:36:29 GMT",
            XmsVersion = "2025-05-05",
            Headers = new Dictionary<string, string>
            {
                ["x-ms-blob-type"] = "BlockBlob",
                ["x-ms-blob-content-type"] = "text/plain",
                ["If-Match"] = "\"0x8D9_TEST_ETAG\"",
                ["Content-Length"] = "19",
                ["Content-Type"] = "application/octet-stream",
                ["x-ms-client-request-id"] = "b9d8ebf6-8218-4627-8eb1-eebddbeaace9",
                ["x-ms-return-client-request-id"] = "true",
            },
        };

        var r = AzureStorageSigner.Sign(req);

        Assert.Equal("SharedKey devstoreaccount1:aHc/i2W3CebKOxBReu3IrDLop8UQhYZpzCpLJRLz+xw=", r.Header.HeaderValue);
    }
}
