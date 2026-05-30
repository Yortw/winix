#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Winix.HCat.Handlers;

/// <summary>Wires the inspect-mode middleware: a single terminal handler that captures every request as a
/// <see cref="RequestRecord"/>, hands it to a record sink (<c>onRecord</c>, wired to JSONL capture + the CI
/// controller in a later task), then echoes the same record back as the response body. Echo body and capture
/// line are the identical serialisation so the two surfaces never drift.</summary>
public static class InspectHandler
{
    /// <summary>Maximum request-body bytes read into the record. Bodies larger than this are read up to the cap,
    /// marked truncated, and still echoed (F8) — the request is never rejected on size alone.</summary>
    private const int BodyCapBytes = 1 * 1024 * 1024;

    /// <summary>Applies the terminal inspect handler to <paramref name="app"/>. Every request — any method,
    /// any path — is captured and echoed with status <see cref="HCatOptions.InspectStatus"/> and
    /// <c>Content-Type: application/json</c>.</summary>
    /// <param name="app">The application to configure.</param>
    /// <param name="o">The parsed options (<see cref="HCatOptions.InspectStatus"/> is used here).</param>
    /// <param name="onRecord">Sink invoked with each captured record before the echo is written (JSONL stdout +
    /// CI controller). Pass a no-op until those are wired.</param>
    public static void Apply(WebApplication app, HCatOptions o, Action<RequestRecord> onRecord)
    {
        app.Run(async context =>
        {
            RequestRecord record = await BuildRecordAsync(context).ConfigureAwait(false);
            onRecord(record);

            string echo = RequestRecord.ToJsonl(record);
            context.Response.StatusCode = o.InspectStatus;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(echo).ConfigureAwait(false);
        });
    }

    /// <summary>Builds a <see cref="RequestRecord"/> from the live request, reading the body up to
    /// <see cref="BodyCapBytes"/> and marking it truncated if it was longer.</summary>
    private static async Task<RequestRecord> BuildRecordAsync(HttpContext context)
    {
        HttpRequest req = context.Request;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> h in req.Headers)
        {
            // Multi-value headers join with ", " (the on-wire combining convention).
            headers[h.Key] = h.Value.ToString();
        }

        (string body, bool truncated) = await ReadBodyAsync(req).ConfigureAwait(false);

        string remote = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        string timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        var record = new RequestRecord(
            Method: req.Method,
            Path: req.Path.HasValue ? req.Path.Value! : "/",
            Query: req.QueryString.HasValue ? req.QueryString.Value!.TrimStart('?') : string.Empty,
            Headers: headers,
            Body: body.Length == 0 ? null : body,
            Timestamp: timestamp,
            RemoteAddr: remote);

        if (truncated)
        {
            record = record with { BodyTruncated = true };
        }
        return record;
    }

    /// <summary>Reads the request body into a string, capping at <see cref="BodyCapBytes"/>. Reads one byte past
    /// the cap to detect (but not retain) overflow, so the truncation flag is accurate without buffering the
    /// whole stream. Never throws on a large body — over-cap input is truncated, not rejected (F8).</summary>
    private static async Task<(string Body, bool Truncated)> ReadBodyAsync(HttpRequest req)
    {
        Stream stream = req.Body;
        var buffer = new byte[BodyCapBytes];
        int total = 0;

        while (total < BodyCapBytes)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total, BodyCapBytes - total)).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            total += read;
        }

        bool truncated = false;
        if (total == BodyCapBytes)
        {
            // Probe one more byte: if anything remains, the body exceeded the cap.
            var probe = new byte[1];
            int extra = await stream.ReadAsync(probe.AsMemory(0, 1)).ConfigureAwait(false);
            truncated = extra > 0;
        }

        string body = Encoding.UTF8.GetString(buffer, 0, total);
        return (body, truncated);
    }
}
