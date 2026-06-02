using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Yort.ShellKit;

/// <summary>
/// Produces stable, human-readable text for an exception destined for user output.
/// </summary>
/// <remarks>
/// Winix tools publish with <c>UseSystemResourceKeys=true</c> (a NativeAOT/trim size
/// optimisation), under which framework exception <see cref="Exception.Message"/> returns the
/// bare CoreLib resource key (e.g. <c>IO_PathNotFound_Path</c>) rather than English. This helper
/// NEVER returns <c>ex.Message</c>: it type-maps the common offenders to project-controlled English
/// and falls back to the exception's type name (context without a leaked key).
/// </remarks>
public static class SafeError
{
    /// <summary>Returns a readable, resource-key-free description of <paramref name="ex"/>.
    /// Null-safe: this runs inside error-reporting paths and must never throw.</summary>
    public static string Describe(Exception? ex)
    {
        if (ex is null) { return "unknown error"; }

        return ex switch
        {
            AggregateException agg when agg.InnerException is not null => Describe(agg.InnerException),
            DirectoryNotFoundException => "no such directory",
            FileNotFoundException => "no such file",
            UnauthorizedAccessException => "access denied",
            PathTooLongException => "path too long",
            RegexParseException rex => $"{rex.Error} at offset {rex.Offset}",
            System.Text.Json.JsonException jex when jex.LineNumber is long line => $"invalid JSON at line {line + 1}",
            System.Text.Json.JsonException => "invalid JSON",
            _ => ex.GetType().Name,
        };
    }
}
