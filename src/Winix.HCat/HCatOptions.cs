#nullable enable
using System;
using System.Collections.Generic;

namespace Winix.HCat;

/// <summary>The selected operating mode.</summary>
public enum HCatMode
{
    /// <summary>Serve static files (default when no subcommand is given).</summary>
    Serve,
    /// <summary>Receive and display/record requests.</summary>
    Inspect,
    /// <summary>Run a command per request (CGI-style).</summary>
    Pipe,
}

/// <summary>Immutable, parsed invocation options for all modes. Produced by <see cref="ArgParser"/>,
/// consumed by <see cref="HCatServer"/>. Defaults encode the safety posture: loopback bind, no upload,
/// 200 inspect status, port 8080.</summary>
public sealed record HCatOptions
{
    /// <summary>The selected mode.</summary>
    public HCatMode Mode { get; init; } = HCatMode.Serve;
    /// <summary>The directory to serve (serve mode); defaults to the current directory.</summary>
    public string Directory { get; init; } = ".";
    /// <summary>Listen port.</summary>
    public int Port { get; init; } = 8080;
    /// <summary>True when <c>--lan</c> was given (bind 0.0.0.0).</summary>
    public bool Lan { get; init; }
    /// <summary>Explicit bind address from <c>--host</c>, or null.</summary>
    public string? Host { get; init; }
    /// <summary>Enable TLS with a self-signed in-memory certificate.</summary>
    public bool Https { get; init; }
    /// <summary>Emit JSONL on stdout instead of human output.</summary>
    public bool Json { get; init; }
    /// <summary>Whether coloured output is enabled.</summary>
    public bool UseColor { get; init; } = true;

    /// <summary>Enable the POST upload receiver (serve mode).</summary>
    public bool Upload { get; init; }
    /// <summary>Upload target directory; null means the default <c>./uploads</c>.</summary>
    public string? UploadDir { get; init; }

    /// <summary>The HTTP status inspect mode responds with (default 200).</summary>
    public int InspectStatus { get; init; } = 200;

    /// <summary>The command + args to run per request (pipe mode).</summary>
    public IReadOnlyList<string> PipeCommand { get; init; } = Array.Empty<string>();

    /// <summary>Exit after this many requests (CI), or null.</summary>
    public int? CaptureCount { get; init; }
    /// <summary>Exit when a request matches this predicate (CI), or null.</summary>
    public string? ExitOn { get; init; }
    /// <summary>Fail/exit if the stop condition is not met within this duration (CI), or null.</summary>
    public TimeSpan? Timeout { get; init; }
}
