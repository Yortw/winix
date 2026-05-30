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

    [Fact]
    public async Task Inspect_echoes_the_request_as_json()
    {
        var (baseUrl, stop) = await StartAsync(new HCatOptions { Mode = HCatMode.Inspect });
        try
        {
            using var http = new HttpClient();
            var resp = await http.PostAsync($"{baseUrl}/hook?a=1",
                new StringContent("{\"k\":1}", System.Text.Encoding.UTF8, "application/json"));
            string body = await resp.Content.ReadAsStringAsync();
            Assert.Contains("\"method\":\"POST\"", body);
            Assert.Contains("\"path\":\"/hook\"", body);
            Assert.Contains("\"query\":\"a=1\"", body);
        }
        finally { await stop(); }
    }

    [Fact]
    public async Task Inspect_status_override_is_honoured()
    {
        var (baseUrl, stop) = await StartAsync(new HCatOptions { Mode = HCatMode.Inspect, InspectStatus = 202 });
        try
        {
            using var http = new HttpClient();
            var resp = await http.GetAsync(baseUrl + "/");
            Assert.Equal(202, (int)resp.StatusCode);
        }
        finally { await stop(); }
    }

    [Fact]   // F8: an over-cap body is truncated (not rejected) and still gets a 2xx echo.
    public async Task Inspect_oversized_body_is_truncated_not_rejected()
    {
        var (baseUrl, stop) = await StartAsync(new HCatOptions { Mode = HCatMode.Inspect });
        try
        {
            using var http = new HttpClient();
            var resp = await http.PostAsync(baseUrl + "/", new StringContent(new string('y', 2 * 1024 * 1024)));
            Assert.Equal(200, (int)resp.StatusCode);
            string body = await resp.Content.ReadAsStringAsync();
            Assert.Contains("\"bodyTruncated\":true", body);
        }
        finally { await stop(); }
    }

    private static string[] EchoCmd()
        => new[] { "dotnet", System.IO.Path.Combine(System.AppContext.BaseDirectory, "HCatEcho.dll") };

    [Fact]
    public async Task Pipe_runs_command_body_to_stdout()
    {
        var (baseUrl, stop) = await StartAsync(new HCatOptions { Mode = HCatMode.Pipe, PipeCommand = EchoCmd() });
        try
        {
            using var http = new HttpClient();
            var resp = await http.PostAsync(baseUrl + "/", new StringContent("ping-body"));
            string body = await resp.Content.ReadAsStringAsync();
            Assert.Contains("ping-body", body);
            Assert.Equal(200, (int)resp.StatusCode);
        }
        finally { await stop(); }
    }

    [Fact]   // F3: a body larger than the OS pipe buffer must not deadlock (concurrent stdin/stdout copy).
    public async Task Pipe_handles_body_larger_than_pipe_buffer_without_deadlock()
    {
        var (baseUrl, stop) = await StartAsync(new HCatOptions { Mode = HCatMode.Pipe, PipeCommand = EchoCmd() });
        try
        {
            string big = new string('x', 256 * 1024);
            using var http = new HttpClient();
            var resp = await http.PostAsync(baseUrl + "/", new StringContent(big))
                .WaitAsync(TimeSpan.FromSeconds(15));   // a deadlock would hang past this
            string echoed = await resp.Content.ReadAsStringAsync();
            Assert.Equal(big.Length, echoed.Length);
        }
        finally { await stop(); }
    }

    [Fact]   // F7: a command that cannot launch must surface as HTTP 500, server stays up.
    public async Task Pipe_command_not_found_returns_500_and_server_survives()
    {
        var (baseUrl, stop) = await StartAsync(new HCatOptions
        {
            Mode = HCatMode.Pipe,
            PipeCommand = new[] { "definitely-not-a-real-command-xyz" },
        });
        try
        {
            using var http = new HttpClient();
            var resp1 = await http.PostAsync(baseUrl + "/", new StringContent("a"));
            Assert.Equal(500, (int)resp1.StatusCode);
            // server still serving:
            var resp2 = await http.PostAsync(baseUrl + "/", new StringContent("b"));
            Assert.Equal(500, (int)resp2.StatusCode);
        }
        finally { await stop(); }
    }
}
