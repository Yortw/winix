using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Winix.HCat;
using Xunit;

namespace Winix.HCat.Tests;

public class IntegrationTests
{
    // Starts the server on an ephemeral loopback port; returns (baseUrl, stopAction).
    private static async Task<(string BaseUrl, Func<Task> Stop)> StartAsync(HCatOptions options)
    {
        var cts = new CancellationTokenSource();
        var ready = new TaskCompletionSource<string>();
        var run = Task.Run(() => HCatServer.RunForTestAsync(options, ready, cts.Token));
        string baseUrl = await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        return (baseUrl, async () => { cts.Cancel(); try { await run; } catch { } });
    }

    [Fact]
    public async Task Serve_returns_a_file()
    {
        string dir = Path.Combine(Path.GetTempPath(), "hcat-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "hello.txt"), "world");
        var (baseUrl, stop) = await StartAsync(new HCatOptions { Mode = HCatMode.Serve, Directory = dir });
        try
        {
            using var http = new HttpClient();
            string body = await http.GetStringAsync($"{baseUrl}/hello.txt");
            Assert.Equal("world", body);
        }
        finally { await stop(); Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Serve_lists_a_directory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "hcat-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.txt"), "x");
        var (baseUrl, stop) = await StartAsync(new HCatOptions { Mode = HCatMode.Serve, Directory = dir });
        try
        {
            using var http = new HttpClient();
            string body = await http.GetStringAsync(baseUrl + "/");
            Assert.Contains("a.txt", body);
        }
        finally { await stop(); Directory.Delete(dir, true); }
    }
}
