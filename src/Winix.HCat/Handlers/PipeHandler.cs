#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Yort.ShellKit;

namespace Winix.HCat.Handlers;

/// <summary>Wires the pipe-mode middleware: a single terminal handler that runs <c>o.PipeCommand[0]</c>
/// (args from <c>[1..]</c>) once per request, CGI-style. The request body is streamed to the child's stdin,
/// the child's stdout is streamed back as the response body, request metadata is passed as CGI environment
/// variables, and the child's stderr goes to the server console (never into the HTTP response).</summary>
/// <remarks>F3 — the stdin and stdout copies run concurrently. A sequential "write all stdin, then read all
/// stdout" approach deadlocks any child that interleaves read and write once an OS pipe buffer fills: the
/// server blocks writing stdin while the child blocks writing stdout that nobody is draining. Starting the
/// stdout→response copy first, then feeding stdin, then awaiting both, lets the child drain freely.</remarks>
public static class PipeHandler
{
    /// <summary>Applies the terminal pipe handler to <paramref name="app"/>. Every request — any method, any
    /// path — spawns the configured command and proxies body/stdout/exit-code.</summary>
    /// <param name="app">The application to configure.</param>
    /// <param name="o">The parsed options; <see cref="HCatOptions.PipeCommand"/> is the command + args.</param>
    /// <param name="onRecord">Sink invoked with the captured record before the child runs (JSONL stdout + CI
    /// controller). Pass a no-op until those are wired.</param>
    public static void Apply(WebApplication app, HCatOptions o, Action<RequestRecord> onRecord)
    {
        app.Run(async context =>
        {
            CancellationToken aborted = context.RequestAborted;

            (RequestRecord record, IReadOnlyDictionary<string, string> headers) = BuildRecord(context);
            onRecord(record);

            IDictionary<string, string> env = CgiEnvironment.Build(
                record.Method, record.Path, record.Query, headers,
                record.RemoteAddr, context.Request.Protocol);

            var psi = new ProcessStartInfo
            {
                FileName = o.PipeCommand[0],
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            // MANDATORY: ArgumentList, never string concatenation (trailing-backslash quoting bugs on Windows).
            for (int i = 1; i < o.PipeCommand.Count; i++)
            {
                psi.ArgumentList.Add(o.PipeCommand[i]);
            }
            foreach (KeyValuePair<string, string> kv in env)
            {
                psi.Environment[kv.Key] = kv.Value;
            }

            Process? child;
            try
            {
                // F7: an absent/unlaunchable command throws Win32Exception. Surface as 500, keep the server up.
                child = Process.Start(psi);
            }
            catch (Win32Exception ex)
            {
                // Status MUST be set before any body write; nothing has been written yet here.
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                Console.Error.WriteLine($"hcat: pipe command failed to start: {o.PipeCommand[0]}: {ex.Message}");
                return;
            }

            if (child is null)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                Console.Error.WriteLine($"hcat: pipe command failed to start: {o.PipeCommand[0]}");
                return;
            }

            using (child)
            {
                // F3: drain the child's stdout into the response FIRST so it can write freely while we feed stdin.
                Task stdoutCopy = child.StandardOutput.BaseStream
                    .CopyToAsync(context.Response.Body, aborted);

                // Forward stderr to the server console only — never into the HTTP response body.
                Task stderrPump = PumpStderrAsync(child.StandardError);

                Task stdinCopy = FeedStdinAsync(context.Request.Body, child.StandardInput, aborted);

                try
                {
                    await Task.WhenAll(stdoutCopy, stdinCopy).ConfigureAwait(false);
                    await child.WaitForExitAsync(aborted).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (aborted.IsCancellationRequested)
                {
                    // Client disconnected mid-stream: kill the whole child process tree, then give up quietly.
                    KillTree(child);
                    return;
                }
                catch (Exception ex)
                {
                    // Any copy/IO failure: try to fail the request (only possible if nothing was written yet)
                    // and ensure the child cannot linger.
                    KillTree(child);
                    if (!context.Response.HasStarted)
                    {
                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    }
                    Console.Error.WriteLine($"hcat: pipe transfer error: {SafeError.Describe(ex)}");
                    return;
                }

                await stderrPump.ConfigureAwait(false);

                // Exit code maps to status, but only if the response hasn't already been committed by the
                // stdout copy. A non-empty child stdout commits a 200 before we see the exit code; in that
                // case we cannot downgrade to 500 (headers are already on the wire).
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = child.ExitCode == 0
                        ? StatusCodes.Status200OK
                        : StatusCodes.Status500InternalServerError;
                }
                else if (child.ExitCode != 0)
                {
                    // The child wrote output (committing a 200) and THEN exited non-zero. We cannot downgrade
                    // the status — headers are already on the wire — but a silent 200 would hide the failure
                    // from the operator running `hcat pipe` for debugging. Surface a breadcrumb to stderr; the
                    // request path is unaffected (diagnostic strictly weaker than the response).
                    Console.Error.WriteLine(
                        $"hcat: pipe command exited {child.ExitCode} after the response was already committed (status remains {context.Response.StatusCode})");
                }
            }
        });
    }

    /// <summary>Streams the full request body to the child's stdin, then closes stdin so the child sees EOF.
    /// Closing the input stream is what lets filters that read-to-end (the common case) terminate.</summary>
    /// <remarks>A child that exits without consuming its whole stdin (a perfectly valid CGI pattern — a handler
    /// that only cares about method/path, or one that early-exits) closes its read end of the pipe. The next
    /// write of the unconsumed body then faults with a broken-pipe <see cref="IOException"/>. That is NOT a
    /// request failure: the child's stdout and exit code determine the response, so the broken pipe is swallowed.
    /// Whether it fires at all depends purely on body size vs the OS pipe buffer — surfacing it as a 500 made an
    /// identical, valid request non-deterministically 200 or 500. A genuine client disconnect is different: it
    /// cancels <paramref name="ct"/>, so we only swallow when cancellation was NOT requested and let a real abort
    /// propagate to the caller's cancellation handler.</remarks>
    private static async Task FeedStdinAsync(Stream requestBody, StreamWriter stdin, CancellationToken ct)
    {
        try
        {
            await requestBody.CopyToAsync(stdin.BaseStream, ct).ConfigureAwait(false);
            await stdin.BaseStream.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (IOException) when (!ct.IsCancellationRequested)
        {
            // Benign: the child closed its stdin early (didn't read the whole body / already exited).
            // The response is governed by the child's stdout + exit code, so this is not a failure.
        }
        finally
        {
            // Signal EOF even if the copy faulted, so the child isn't left blocked on a never-closing stdin.
            try
            {
                stdin.Close();
            }
            catch
            {
                // The child may have already exited and closed its end; nothing to do.
            }
        }
    }

    /// <summary>Forwards the child's stderr to the server console. Best-effort: a stderr-pump failure must
    /// never fault the request.</summary>
    private static async Task PumpStderrAsync(StreamReader stderr)
    {
        try
        {
            string text = await stderr.ReadToEndAsync().ConfigureAwait(false);
            if (text.Length > 0)
            {
                await Console.Error.WriteAsync(text).ConfigureAwait(false);
            }
        }
        catch
        {
            // Diagnostic path is strictly weaker than the request path: never propagate.
        }
    }

    /// <summary>Kills the child and its descendants, swallowing the races inherent in tearing down a process
    /// that may already have exited.</summary>
    private static void KillTree(Process child)
    {
        try
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Already exited / not killable: nothing useful to do.
        }
    }

    /// <summary>Builds the <see cref="RequestRecord"/> for the record sink and returns the request headers
    /// (used both for the record and for the CGI environment).</summary>
    /// <remarks>The request body is deliberately NOT read into the record: pipe mode streams the live body
    /// straight to the child's stdin, and reading it here would consume the stream so the child would receive
    /// nothing. The record's <c>Body</c> is left null; the body is observable in the child's behaviour instead.
    /// </remarks>
    private static (RequestRecord Record, IReadOnlyDictionary<string, string> Headers) BuildRecord(
        HttpContext context)
    {
        HttpRequest req = context.Request;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> h in req.Headers)
        {
            headers[h.Key] = h.Value.ToString();
        }

        string remote = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        string timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        var record = new RequestRecord(
            Method: req.Method,
            Path: req.Path.HasValue ? req.Path.Value! : "/",
            Query: req.QueryString.HasValue ? req.QueryString.Value!.TrimStart('?') : string.Empty,
            Headers: headers,
            Body: null,
            Timestamp: timestamp,
            RemoteAddr: remote);

        return (record, headers);
    }
}
