#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Winix.Notify;

/// <summary>
/// Sends a push notification to an ntfy.sh-compatible server (hosted or self-hosted).
/// Stateless — owns no I/O state; the <see cref="HttpClient"/> is injected so tests can supply a fake handler.
/// </summary>
public sealed class NtfyBackend : IBackend
{
    private readonly HttpClient _http;
    private readonly string _server;
    private readonly string _topic;
    private readonly string? _token;

    /// <inheritdoc />
    public string Name => "ntfy";

    /// <summary>Creates a backend bound to a specific server + topic.</summary>
    /// <param name="http">HttpClient (production: a static singleton; tests: with a fake handler).</param>
    /// <param name="server">Server base URL, e.g. https://ntfy.sh. Trailing slash optional.</param>
    /// <param name="topic">Topic name. ArgParser validates non-empty upstream.</param>
    /// <param name="token">Optional bearer token for self-hosted ntfy with access control.</param>
    public NtfyBackend(HttpClient http, string server, string topic, string? token)
    {
        _http = http;
        _server = server.TrimEnd('/');
        _topic = topic;
        _token = token;
    }

    /// <inheritdoc />
    public async Task<BackendResult> SendAsync(NotifyMessage message, CancellationToken ct)
    {
        var detail = new Dictionary<string, string>
        {
            ["server"] = _server,
            ["topic"] = _topic,
        };

        try
        {
            string url = $"{_server}/{_topic}";
            using var request = new HttpRequestMessage(HttpMethod.Post, url);

            // ntfy requires a non-empty body — when no body provided, send title as body.
            string bodyText = message.Body ?? message.Title;
            request.Content = new StringContent(bodyText, Encoding.UTF8, "text/plain");
            // Strip CR/LF from the Title header value — TryAddWithoutValidation skips the
            // built-in newline check, so a title containing "\r\n" would inject extra headers.
            request.Headers.TryAddWithoutValidation("Title", SingleLine(message.Title));
            request.Headers.TryAddWithoutValidation("Priority", PriorityFor(message.Urgency));
            if (_token is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            }

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                int code = (int)response.StatusCode;
                // Round-1 review M1 — surface the response body (capped to keep diagnostics
                // bounded) so users see the server's "topic requires auth" or "rate limited"
                // explanation instead of just the status code.
                string bodyHint = await TryReadBodySnippetAsync(response, ct).ConfigureAwait(false);
                string suffix = bodyHint.Length > 0 ? $" — {bodyHint}" : "";
                return new BackendResult(Name, false, $"ntfy POST failed: {code} {response.ReasonPhrase}{suffix}", detail);
            }

            return new BackendResult(Name, true, null, detail);
        }
        // Round-1 review SFH-I2 — widen the catch to the generic IBackend never-throw contract.
        // The previous filter (HttpRequestException or TaskCanceledException) let
        // UriFormatException / ArgumentException from a malformed --ntfy-server escape, fault
        // Task.WhenAll in Dispatcher, and discard sibling backends' successful results — so a
        // typo'd ntfy server URL would mask a successful desktop notification with a generic
        // process-crash message. Match peer backends (Linux/macOS/Windows) which catch broadly.
        // Round-3 review (SFH M1) — the previous round-1 M2 OperationCanceledException re-throw
        // arm was removed. After R2-I1 the dispatcher converts OCE to a per-backend "cancelled
        // before completion" result anyway, so the per-backend re-throw was redundant — and
        // asymmetric with peer backends (Linux/macOS/Windows) which fall through their broad
        // catches. Letting OCE land in the generic catch here produces an identical-shape
        // result; the dispatcher's OCE-conversion path remains the single source of truth.
        catch (Exception ex)
        {
            return new BackendResult(Name, false, $"ntfy POST failed: {ex.GetType().Name}: {ex.Message}", detail);
        }
    }

    // Round-2 review R2-I2 — bounded stream read replaces ReadAsStringAsync.
    // The previous implementation buffered the entire response body before applying the
    // 512-char cap; a hostile or misconfigured ntfy endpoint returning a multi-GB 4xx body
    // would OOM the process before the cap took effect, violating the "never throws" contract.
    // Now: read at most 2 KB from the response stream, then UTF-8 decode and trim/cap.
    // Always returns "" on any error so the 4xx/5xx report never becomes something worse.
    private const int MaxBodySnippetBytes = 2048;
    private const int MaxBodySnippetChars = 512;

    private static async Task<string> TryReadBodySnippetAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
#pragma warning disable CA2016 // ReadAsStreamAsync's CancellationToken overload may not exist on every runtime; fallback is fine.
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
#pragma warning restore CA2016
            byte[] buffer = new byte[MaxBodySnippetBytes];
            int read = 0;
            while (read < MaxBodySnippetBytes)
            {
                int n = await stream.ReadAsync(buffer.AsMemory(read, MaxBodySnippetBytes - read), ct).ConfigureAwait(false);
                if (n == 0) break;
                read += n;
            }
            if (read == 0) return "";
            string body = Encoding.UTF8.GetString(buffer, 0, read);
            string trimmed = body.Trim();
            if (trimmed.Length == 0) return "";
            return trimmed.Length > MaxBodySnippetChars ? trimmed.Substring(0, MaxBodySnippetChars) + "…" : trimmed;
        }
        catch
        {
            return "";
        }
    }

    private static string PriorityFor(Urgency urgency) => urgency switch
    {
        Urgency.Low => "2",
        Urgency.Normal => "3",
        Urgency.Critical => "5",
        _ => "3",
    };

    // Replace CR / LF with spaces — header values must be single-line.
    private static string SingleLine(string s) => s.Replace('\r', ' ').Replace('\n', ' ');
}
