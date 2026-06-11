#nullable enable

using System;
using Winix.Online;
using Xunit;

namespace Winix.Online.Tests;

public class OnlineOptionsTests
{
    [Fact]
    public void Stores_values_verbatim()
    {
        var opts = new OnlineOptions(
            checkInternet: true,
            urls: new[] { "https://a" },
            status: StatusSpec.Default,
            endpoints: DefaultEndpoints.All,
            timeout: TimeSpan.FromMinutes(10),
            interval: TimeSpan.FromSeconds(2),
            probeTimeout: TimeSpan.FromSeconds(3),
            once: false,
            verbose: true);

        Assert.True(opts.CheckInternet);
        Assert.Single(opts.Urls);
        Assert.Equal(TimeSpan.FromMinutes(10), opts.Timeout);
        Assert.True(opts.Verbose);
    }

    [Fact]
    public void Default_endpoint_list_is_all_204_style_urls()
    {
        Assert.NotEmpty(DefaultEndpoints.All);
        foreach (string url in DefaultEndpoints.All)
        {
            Assert.True(Uri.TryCreate(url, UriKind.Absolute, out Uri? u) && u.Scheme == "https",
                $"endpoint must be an absolute https URL: {url}");
            Assert.Contains("generate_204", url, StringComparison.Ordinal);
        }
    }
}
