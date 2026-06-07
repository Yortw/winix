using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
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

    // Round-2 TA finding: the duplicate-query-key branch (values ordinal-sorted, comma-joined) was
    // documented but had no asserting test, and it CANNOT be SDK-captured — Azure.Storage.Blobs'
    // URI handling rejects duplicate query keys. Oracle here is a hand-written StringToSign literal
    // (composed from Microsoft's documented layout, NOT by calling the signer) HMAC'd with the
    // framework primitive; the ordering asserts make the sort+join the requirement under test.
    [Fact]
    public void Duplicate_query_keys_are_value_sorted_and_comma_joined()
    {
        var req = new AzureStorageRequest
        {
            Account = "devstoreaccount1",
            KeyBase64 = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==",
            Method = "GET",
            // sv supplied b-then-a: the canonicalized resource must emit "sv:a,b" (values re-sorted).
            Url = "https://devstoreaccount1.blob.core.windows.net/mycontainer/myblob.txt?sv=b&sv=a",
            XmsDate = "Fri, 26 Jun 2026 00:00:00 GMT",
            XmsVersion = "2021-08-06",
            Headers = new Dictionary<string, string>(),
        };

        var r = AzureStorageSigner.Sign(req);

        // The requirement: duplicate values sorted then comma-joined, never input order.
        Assert.Contains("\nsv:a,b", r.StringToSign, StringComparison.Ordinal);
        Assert.DoesNotContain("sv:b,a", r.StringToSign, StringComparison.Ordinal);

        // Full-signature pin from a hand-built StringToSign (12 fixed slots, GET with no content or
        // conditional headers, x-ms-date in the canonicalized block so the Date slot is empty).
        string handBuilt =
            "GET\n\n\n\n\n\n\n\n\n\n\n\n" +
            "x-ms-date:Fri, 26 Jun 2026 00:00:00 GMT\n" +
            "x-ms-version:2021-08-06\n" +
            "/devstoreaccount1/mycontainer/myblob.txt\nsv:a,b";
        using var hmac = new HMACSHA256(Convert.FromBase64String(req.KeyBase64));
        string expectedSig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(handBuilt)));

        Assert.Equal("SharedKey devstoreaccount1:" + expectedSig, r.Header.HeaderValue);
    }
}
