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
            request.Headers.TryAddWithoutValidation("Title", message.Title);
            request.Headers.TryAddWithoutValidation("Priority", PriorityFor(message.Urgency));
            if (_token is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            }

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                int code = (int)response.StatusCode;
                return new BackendResult(Name, false, $"ntfy POST failed: {code} {response.ReasonPhrase}", detail);
            }

            return new BackendResult(Name, true, null, detail);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new BackendResult(Name, false, $"ntfy POST failed: {ex.Message}", detail);
        }
    }

    private static string PriorityFor(Urgency urgency) => urgency switch
    {
        Urgency.Low => "2",
        Urgency.Normal => "3",
        Urgency.Critical => "5",
        _ => "3",
    };
}
