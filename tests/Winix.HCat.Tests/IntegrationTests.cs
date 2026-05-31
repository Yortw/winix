using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
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

    [Fact]   // T15: serve mode over TLS using the in-memory self-signed cert.
    public async Task Serve_returns_a_file_over_https()
    {
        string dir = Path.Combine(Path.GetTempPath(), "hcat-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "secure.txt"), "tls-world");
        var (baseUrl, stop) = await StartAsync(new HCatOptions
        {
            Mode = HCatMode.Serve, Directory = dir, Https = true,
        });
        try
        {
            Assert.StartsWith("https://", baseUrl);
            // Self-signed certs aren't trusted; bypass validation (expected for dev/LAN).
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            };
            using var http = new HttpClient(handler);
            string body = await http.GetStringAsync($"{baseUrl}/secure.txt");
            Assert.Equal("tls-world", body);
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

    [Fact]   // A clean (exit 0) command that ignores a large body must still return 200, NOT a spurious 500.
    public async Task Pipe_command_that_ignores_large_body_still_returns_200()
    {
        // --no-read exits 0 without consuming stdin. With a body larger than the OS pipe buffer, the server's
        // stdin feed faults with a broken pipe once the child closes its end — which used to be caught and
        // forced to 500, making the status depend on body size. The fix treats that broken pipe as benign.
        var cmd = new[]
        {
            "dotnet",
            System.IO.Path.Combine(System.AppContext.BaseDirectory, "HCatEcho.dll"),
            "--no-read",
        };
        var (baseUrl, stop) = await StartAsync(new HCatOptions { Mode = HCatMode.Pipe, PipeCommand = cmd });
        try
        {
            string big = new string('x', 4 * 1024 * 1024);   // 4 MiB — overflows the pipe buffer
            using var http = new HttpClient();
            var resp = await http.PostAsync(baseUrl + "/", new StringContent(big))
                .WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(200, (int)resp.StatusCode);
            Assert.Equal("ok", await resp.Content.ReadAsStringAsync());
        }
        finally { await stop(); }
    }

    [Fact]   // A non-zero exit AFTER output has committed a 200 cannot downgrade the status; server survives.
    public async Task Pipe_nonzero_exit_after_committed_output_keeps_200_and_survives()
    {
        var cmd = new[]
        {
            "dotnet",
            System.IO.Path.Combine(System.AppContext.BaseDirectory, "HCatEcho.dll"),
            "--fail-after-output",
        };
        var (baseUrl, stop) = await StartAsync(new HCatOptions { Mode = HCatMode.Pipe, PipeCommand = cmd });
        try
        {
            using var http = new HttpClient();
            var resp = await http.PostAsync(baseUrl + "/", new StringContent("x"));
            // Headers are already on the wire by the time the non-zero exit is observed, so the status stays
            // 200 (the operator gets a stderr breadcrumb — diagnostic only, not asserted here). The contract
            // pinned here: the response body is intact and the server keeps serving the next request.
            Assert.Equal(200, (int)resp.StatusCode);
            Assert.Equal("partial-output", await resp.Content.ReadAsStringAsync());
            var resp2 = await http.PostAsync(baseUrl + "/", new StringContent("y"));
            Assert.Equal(200, (int)resp2.StatusCode);
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

    // (a) --upload saves a POSTed file into the default ./uploads (relative to the served dir) and the
    // saved file is NOT downloadable (the upload sub-tree is excluded from serving).
    [Fact]
    public async Task Upload_saves_to_default_uploads_and_is_not_downloadable()
    {
        string dir = Path.Combine(Path.GetTempPath(), "hcat-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var (baseUrl, stop) = await StartAsync(new HCatOptions
        {
            Mode = HCatMode.Serve, Directory = dir, Upload = true,
        });
        try
        {
            using var http = new HttpClient();
            var post = await http.PostAsync($"{baseUrl}/?filename=note.txt", new StringContent("payload-a"));
            Assert.Equal(201, (int)post.StatusCode);

            // Saved into <served>/uploads/note.txt on disk.
            string saved = Path.Combine(dir, "uploads", "note.txt");
            Assert.True(File.Exists(saved), "uploaded file should be saved under ./uploads");
            Assert.Equal("payload-a", File.ReadAllText(saved));

            // But NOT downloadable — the upload sub-tree is excluded from serving.
            var get = await http.GetAsync($"{baseUrl}/uploads/note.txt");
            Assert.Equal(404, (int)get.StatusCode);
        }
        finally { await stop(); Directory.Delete(dir, true); }
    }

    // (b) --upload-dir . saves into the served root and the file IS downloadable (the documented escape hatch).
    [Fact]
    public async Task Upload_into_served_root_is_downloadable()
    {
        string dir = Path.Combine(Path.GetTempPath(), "hcat-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var (baseUrl, stop) = await StartAsync(new HCatOptions
        {
            Mode = HCatMode.Serve, Directory = dir, Upload = true, UploadDir = dir,
        });
        try
        {
            using var http = new HttpClient();
            var post = await http.PostAsync($"{baseUrl}/?filename=open.txt", new StringContent("payload-b"));
            Assert.Equal(201, (int)post.StatusCode);

            string saved = Path.Combine(dir, "open.txt");
            Assert.True(File.Exists(saved), "uploaded file should be saved into the served root");
            Assert.Equal("payload-b", File.ReadAllText(saved));

            // The escape hatch: a file in the served root IS downloadable.
            string body = await http.GetStringAsync($"{baseUrl}/open.txt");
            Assert.Equal("payload-b", body);
        }
        finally { await stop(); Directory.Delete(dir, true); }
    }

    // (c) a ..-laden filename is rejected (400) and nothing is written outside the upload root.
    [Fact]
    public async Task Upload_rejects_traversal_filename_and_writes_nothing_outside_root()
    {
        string dir = Path.Combine(Path.GetTempPath(), "hcat-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        // A sentinel sibling we must prove stays untouched (the traversal target would land here).
        string outsidePath = Path.Combine(dir, "escaped.txt");
        var (baseUrl, stop) = await StartAsync(new HCatOptions
        {
            Mode = HCatMode.Serve, Directory = dir, Upload = true,
        });
        try
        {
            using var http = new HttpClient();
            var post = await http.PostAsync(
                $"{baseUrl}/?filename={Uri.EscapeDataString("../escaped.txt")}",
                new StringContent("evil"));
            Assert.Equal(400, (int)post.StatusCode);

            Assert.False(File.Exists(outsidePath), "traversal target must not be written outside the upload root");
        }
        finally { await stop(); Directory.Delete(dir, true); }
    }

    // (d) F1 ordering guard: a file physically pre-placed at <served>/uploads/ before the server starts must
    // still 404 with --upload — proving the exclusion middleware wins over UseStaticFiles (which would otherwise
    // serve it 200). This is the load-bearing secure-by-default ordering invariant.
    // --- Task 16: CI lifecycle + exit codes ---

    // Starts RunForTestAsync with a JSONL sink and CI stop conditions. Returns (baseUrl, runTask) where
    // runTask is the server's own task — it COMPLETES on its own once a stop condition fires, returning the
    // outcome exit code. The caller cancels via the CTS for the non-self-terminating cases.
    private static async Task<(string BaseUrl, Task<int> Run, CancellationTokenSource Cts)> StartWithSinkAsync(
        HCatOptions options, TextWriter jsonSink)
    {
        var cts = new CancellationTokenSource();
        var ready = new TaskCompletionSource<string>();
        Task<int> run = Task.Run(() => HCatServer.RunForTestAsync(options, ready, jsonSink, cts.Token));
        string baseUrl = await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        return (baseUrl, run, cts);
    }

    [Fact]   // capture 1: server completes on its own after one request; the JSONL line reaches the sink.
    public async Task Inspect_capture_one_completes_on_its_own_and_writes_jsonl()
    {
        var sink = new StringWriter();
        var (baseUrl, run, cts) = await StartWithSinkAsync(
            new HCatOptions { Mode = HCatMode.Inspect, CaptureCount = 1 }, sink);
        try
        {
            using var http = new HttpClient();
            await http.PostAsync($"{baseUrl}/hook?a=1",
                new StringContent("{\"k\":1}", System.Text.Encoding.UTF8, "application/json"));

            // The server must shut down on its own (no Cts.Cancel) — capture 1 was satisfied.
            int code = await run.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, code);

            string captured = sink.ToString();
            Assert.Contains("\"method\":\"POST\"", captured);
            Assert.Contains("\"path\":\"/hook\"", captured);
        }
        finally { cts.Cancel(); try { await run; } catch { } }
    }

    [Fact]   // F6: the request that TRIGGERS the stop still receives its full echo body + correct status.
    public async Task Inspect_capture_one_triggering_request_still_gets_full_response()
    {
        var sink = new StringWriter();
        var (baseUrl, run, cts) = await StartWithSinkAsync(
            new HCatOptions { Mode = HCatMode.Inspect, CaptureCount = 1, InspectStatus = 202 }, sink);
        try
        {
            using var http = new HttpClient();
            var resp = await http.PostAsync($"{baseUrl}/trigger",
                new StringContent("payload"));
            // The triggering response must not be truncated by the stop.
            Assert.Equal(202, (int)resp.StatusCode);
            string body = await resp.Content.ReadAsStringAsync();
            Assert.Contains("\"method\"", body);
            Assert.Contains("\"path\":\"/trigger\"", body);

            int code = await run.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, code);
        }
        finally { cts.Cancel(); try { await run; } catch { } }
    }

    [Fact]   // #2: serve --json emits a per-request access-log line {method,path,status} to stdout, and serve
             // now honours --capture (the server self-terminates after N requests, exit 0).
    public async Task Serve_json_emits_access_log_and_capture_stops()
    {
        string dir = Path.Combine(Path.GetTempPath(), "hcat-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "hello.txt"), "world");
        var sink = new StringWriter();
        var (baseUrl, run, cts) = await StartWithSinkAsync(
            new HCatOptions { Mode = HCatMode.Serve, Directory = dir, CaptureCount = 1 }, sink);
        try
        {
            using var http = new HttpClient();
            Assert.Equal("world", await http.GetStringAsync($"{baseUrl}/hello.txt"));

            int code = await run.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, code);

            string log = sink.ToString();
            Assert.Contains("\"method\":\"GET\"", log);
            Assert.Contains("\"path\":\"/hello.txt\"", log);
            Assert.Contains("\"status\":200", log);
        }
        finally { cts.Cancel(); try { await run; } catch { } Directory.Delete(dir, true); }
    }

    [Fact]   // #2: serve --exit-on path= matches on the request path and self-terminates the server (exit 0).
    public async Task Serve_exit_on_path_stops_on_match()
    {
        string dir = Path.Combine(Path.GetTempPath(), "hcat-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "done.txt"), "ok");
        var sink = new StringWriter();
        var (baseUrl, run, cts) = await StartWithSinkAsync(
            new HCatOptions { Mode = HCatMode.Serve, Directory = dir, ExitOn = "path=/done.txt" }, sink);
        try
        {
            using var http = new HttpClient();
            await http.GetAsync($"{baseUrl}/done.txt");
            int code = await run.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, code);
        }
        finally { cts.Cancel(); try { await run; } catch { } Directory.Delete(dir, true); }
    }

    [Fact]   // F9: --capture 1 --timeout with NO request → server stops, exit code 1.
    public async Task Inspect_timeout_without_request_yields_exit_1()
    {
        var sink = new StringWriter();
        var (baseUrl, run, cts) = await StartWithSinkAsync(
            new HCatOptions
            {
                Mode = HCatMode.Inspect, CaptureCount = 1, Timeout = TimeSpan.FromSeconds(1),
            },
            sink);
        try
        {
            // Send nothing; the 1s timeout must fire, the server self-stops, and the outcome is 1.
            int code = await run.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, code);
        }
        finally { cts.Cancel(); try { await run; } catch { } }
    }

    // --- serve --spa fallback ---

    // Builds a temp served dir with index.html ("INDEX"), app.html ("APP"), real.txt ("REAL"),
    // and an empty subdir "sub". Returns the dir path (caller deletes).
    private static string MakeSpaDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "hcat-spa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "index.html"), "INDEX");
        File.WriteAllText(Path.Combine(dir, "app.html"), "APP");
        File.WriteAllText(Path.Combine(dir, "real.txt"), "REAL");
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        return dir;
    }

    private static async Task<(int Status, string Body)> GetWithAccept(string url, string accept)
    {
        using var http = new HttpClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Clear();
        req.Headers.Accept.ParseAdd(accept);
        using HttpResponseMessage resp = await http.SendAsync(req);
        return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    [Fact]   // deep-link navigation (Accept: text/html) for a non-file path -> 200 index shell.
    public async Task Spa_navigation_deeplink_returns_index()
    {
        string dir = MakeSpaDir();
        var (baseUrl, stop) = await StartAsync(new HCatOptions { Mode = HCatMode.Serve, Directory = dir, Spa = true });
        try
        {
            var (status, body) = await GetWithAccept($"{baseUrl}/users/42", "text/html");
            Assert.Equal(200, status);
            Assert.Equal("INDEX", body);
        }
        finally { await stop(); Directory.Delete(dir, true); }
    }

    [Fact]   // a missing asset fetched as */* keeps its real 404 (no HTML served as JS).
    public async Task Spa_missing_asset_with_wildcard_accept_returns_404()
    {
        string dir = MakeSpaDir();
        var (baseUrl, stop) = await StartAsync(new HCatOptions { Mode = HCatMode.Serve, Directory = dir, Spa = true });
        try
        {
            var (status, _) = await GetWithAccept($"{baseUrl}/missing.js", "*/*");
            Assert.Equal(404, status);
        }
        finally { await stop(); Directory.Delete(dir, true); }
    }

    [Fact]   // an existing real file is served normally — fallback never overrides it.
    public async Task Spa_real_file_is_served_normally()
    {
        string dir = MakeSpaDir();
        var (baseUrl, stop) = await StartAsync(new HCatOptions { Mode = HCatMode.Serve, Directory = dir, Spa = true });
        try
        {
            var (status, body) = await GetWithAccept($"{baseUrl}/real.txt", "text/html");
            Assert.Equal(200, status);
            Assert.Equal("REAL", body);
        }
        finally { await stop(); Directory.Delete(dir, true); }
    }

    [Fact]   // root still serves index.html via the default-document mapping.
    public async Task Spa_root_serves_index()
    {
        string dir = MakeSpaDir();
        var (baseUrl, stop) = await StartAsync(new HCatOptions { Mode = HCatMode.Serve, Directory = dir, Spa = true });
        try
        {
            var (status, body) = await GetWithAccept($"{baseUrl}/", "text/html");
            Assert.Equal(200, status);
            Assert.Equal("INDEX", body);
        }
        finally { await stop(); Directory.Delete(dir, true); }
    }

    [Fact]   // directory browsing is OFF in SPA mode: an existing indexless dir navigation -> index shell, not a listing.
    public async Task Spa_existing_dir_without_index_returns_index_not_listing()
    {
        string dir = MakeSpaDir();
        var (baseUrl, stop) = await StartAsync(new HCatOptions { Mode = HCatMode.Serve, Directory = dir, Spa = true });
        try
        {
            var (status, body) = await GetWithAccept($"{baseUrl}/sub/", "text/html");
            Assert.Equal(200, status);
            Assert.Equal("INDEX", body);   // the shell, NOT an auto directory listing
        }
        finally { await stop(); Directory.Delete(dir, true); }
    }

    [Fact]   // --spa-index selects a different shell file.
    public async Task Spa_custom_index_file_is_used()
    {
        string dir = MakeSpaDir();
        var (baseUrl, stop) = await StartAsync(new HCatOptions
        {
            Mode = HCatMode.Serve, Directory = dir, Spa = true, SpaIndexFile = "app.html",
        });
        try
        {
            var (status, body) = await GetWithAccept($"{baseUrl}/deep/link", "text/html");
            Assert.Equal(200, status);
            Assert.Equal("APP", body);
        }
        finally { await stop(); Directory.Delete(dir, true); }
    }

    [Fact]   // --upload exclusion wins over SPA fallback: a GET under the excluded upload prefix stays 404.
    public async Task Spa_with_upload_excluded_path_stays_404()
    {
        string dir = MakeSpaDir();
        var (baseUrl, stop) = await StartAsync(new HCatOptions
        {
            Mode = HCatMode.Serve, Directory = dir, Spa = true, Upload = true,
        });
        try
        {
            // default upload dir is <dir>/uploads, excluded from serving; a navigation there must NOT get the shell.
            var (status, _) = await GetWithAccept($"{baseUrl}/uploads/whatever", "text/html");
            Assert.Equal(404, status);
        }
        finally { await stop(); Directory.Delete(dir, true); }
    }

    [Fact]   // F4: a missing API call (specific non-HTML Accept) keeps its real 404 — no shell served for it.
    public async Task Spa_missing_api_with_json_accept_returns_404()
    {
        string dir = MakeSpaDir();
        var (baseUrl, stop) = await StartAsync(new HCatOptions { Mode = HCatMode.Serve, Directory = dir, Spa = true });
        try
        {
            var (status, _) = await GetWithAccept($"{baseUrl}/api/thing", "application/json");
            Assert.Equal(404, status);
        }
        finally { await stop(); Directory.Delete(dir, true); }
    }

    [Fact]   // F5: --spa with NO shell file present -> navigations 404 (not 500, not a hang).
    public async Task Spa_without_index_file_navigation_returns_404()
    {
        string dir = Path.Combine(Path.GetTempPath(), "hcat-spa-noidx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);   // deliberately no index.html
        var (baseUrl, stop) = await StartAsync(new HCatOptions { Mode = HCatMode.Serve, Directory = dir, Spa = true });
        try
        {
            var (status, _) = await GetWithAccept($"{baseUrl}/users/42", "text/html");
            Assert.Equal(404, status);
        }
        finally { await stop(); Directory.Delete(dir, true); }
    }

    [Fact]   // F6: HEAD navigation returns 200 + Content-Length, no body (the HEAD branch of the fallback).
    public async Task Spa_head_navigation_returns_headers_no_body()
    {
        string dir = MakeSpaDir();
        var (baseUrl, stop) = await StartAsync(new HCatOptions { Mode = HCatMode.Serve, Directory = dir, Spa = true });
        try
        {
            using var http = new HttpClient();
            using var req = new HttpRequestMessage(HttpMethod.Head, $"{baseUrl}/deep/route");
            req.Headers.Accept.ParseAdd("text/html");
            using HttpResponseMessage resp = await http.SendAsync(req);
            Assert.Equal(200, (int)resp.StatusCode);
            Assert.Equal(5, resp.Content.Headers.ContentLength);   // "INDEX"
            Assert.Equal("", await resp.Content.ReadAsStringAsync());   // HEAD: no body
        }
        finally { await stop(); Directory.Delete(dir, true); }
    }

    [Fact]   // F7: a port already in use → Cli.Run returns 126 with a human-readable stderr message.
    public void Bind_failure_returns_126_with_human_message()
    {
        // Hold a loopback port so the server's bind fails with address-in-use.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            int code = Cli.Run(
                new[] { "serve", "--host", "127.0.0.1", "--port", port.ToString() }, stdout, stderr);
            Assert.Equal(126, code);
            string err = stderr.ToString();
            Assert.Contains("hcat: cannot bind", err);
            Assert.Contains("address already in use", err);
            // Must NOT leak an SR key.
            Assert.DoesNotContain("_Arg_", err);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task Upload_excludes_preplaced_file_under_upload_subtree()
    {
        string dir = Path.Combine(Path.GetTempPath(), "hcat-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string uploads = Path.Combine(dir, "uploads");
        Directory.CreateDirectory(uploads);
        File.WriteAllText(Path.Combine(uploads, "planted.txt"), "planted-content");

        var (baseUrl, stop) = await StartAsync(new HCatOptions
        {
            Mode = HCatMode.Serve, Directory = dir, Upload = true,
        });
        try
        {
            using var http = new HttpClient();
            var get = await http.GetAsync($"{baseUrl}/uploads/planted.txt");
            Assert.Equal(404, (int)get.StatusCode);
        }
        finally { await stop(); Directory.Delete(dir, true); }
    }
}
