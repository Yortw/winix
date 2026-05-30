#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Winix.HCat.Handlers;

/// <summary>Wires the serve-mode upload receiver: a middleware that, for POST requests, streams the request
/// body to a file inside the resolved upload root, using <see cref="UploadPathSafety.ResolveTarget"/> so a
/// hostile filename (<c>..</c>, directory components, absolute paths) cannot escape the root. Non-POST requests
/// fall through to the next middleware (static file serving). The companion download-exclusion (404ing GETs
/// under an in-tree upload sub-path) lives in <see cref="ServeConfig"/> — and is registered <em>before</em> the
/// static-file middleware so the secure-by-default invariant holds.</summary>
public static class UploadHandler
{
    /// <summary>Registers the POST upload middleware on <paramref name="app"/>. The upload root is
    /// <c>o.UploadDir ?? &lt;served&gt;/uploads</c> (created if missing). The uploaded filename is taken from the
    /// <c>filename</c> query parameter, falling back to the <c>Content-Disposition</c> header. A filename that
    /// carries directory components or a <c>..</c> segment, or that path-safety rejects, returns 400 with nothing
    /// written; a saved file returns 201 with the saved leaf name. Non-POST requests pass through untouched.</summary>
    /// <param name="app">The application to configure.</param>
    /// <param name="o">The parsed options; <see cref="HCatOptions.Directory"/> and
    /// <see cref="HCatOptions.UploadDir"/> determine the upload root.</param>
    /// <remarks>Implemented as plain middleware rather than an endpoint-routed <c>MapPost</c>: a catch-all POST
    /// route would make endpoint routing return 405 for GETs to the same path, short-circuiting the file-server
    /// fall-through. Method-gated middleware avoids that entirely.</remarks>
    public static void Apply(WebApplication app, HCatOptions o)
    {
        string uploadRoot = ResolveUploadRoot(o);
        // Create up front so the receiver never races on first POST; harmless if it already exists.
        Directory.CreateDirectory(uploadRoot);

        app.Use(async (context, next) =>
        {
            if (!HttpMethods.IsPost(context.Request.Method))
            {
                await next().ConfigureAwait(false);
                return;
            }

            string? fileName = ResolveFileName(context.Request);

            // Reject directory-laden or traversal names explicitly (400) rather than silently sanitising to the
            // leaf — the caller asked for a path we will not honour. ResolveTarget remains the defence-in-depth
            // backstop below.
            if (fileName is null || HasPathComponents(fileName))
            {
                await Reject(context).ConfigureAwait(false);
                return;
            }

            string? target = UploadPathSafety.ResolveTarget(uploadRoot, fileName, File.Exists);
            if (target is null)
            {
                await Reject(context).ConfigureAwait(false);
                return;
            }

            // Stream straight to disk — never buffer the whole body in memory. CreateNew so a concurrent racer
            // cannot have the resolved-unique name stolen between ResolveTarget and the open.
            await using (FileStream fs = new FileStream(
                target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await context.Request.Body.CopyToAsync(fs).ConfigureAwait(false);
            }

            context.Response.StatusCode = StatusCodes.Status201Created;
            await context.Response.WriteAsync(Path.GetFileName(target)).ConfigureAwait(false);
        });
    }

    /// <summary>Resolves the absolute upload root: the explicit <c>--upload-dir</c> if given, else
    /// <c>&lt;served&gt;/uploads</c>.</summary>
    public static string ResolveUploadRoot(HCatOptions o)
    {
        string raw = o.UploadDir ?? Path.Combine(o.Directory, "uploads");
        return Path.GetFullPath(raw);
    }

    /// <summary>True when the filename carries any directory component, a <c>..</c> segment, or is rooted —
    /// i.e. it is not a plain leaf name and must be rejected outright.</summary>
    private static bool HasPathComponents(string fileName)
    {
        if (fileName.IndexOf('/') >= 0 || fileName.IndexOf('\\') >= 0) { return true; }
        if (Path.IsPathRooted(fileName)) { return true; }
        if (fileName == "." || fileName == "..") { return true; }
        return false;
    }

    /// <summary>Writes a 400 rejection response. Nothing is written to disk.</summary>
    private static async Task Reject(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Rejected upload filename.").ConfigureAwait(false);
    }

    /// <summary>Extracts the uploaded filename from the <c>filename</c> query parameter, falling back to the
    /// <c>Content-Disposition</c> header's <c>filename</c> attribute. Returns null when neither is present;
    /// path-safety is enforced by the caller plus <see cref="UploadPathSafety.ResolveTarget"/>.</summary>
    private static string? ResolveFileName(HttpRequest req)
    {
        if (req.Query.TryGetValue("filename", out var q) && !string.IsNullOrWhiteSpace(q))
        {
            return q.ToString();
        }

        string? disposition = req.Headers.ContentDisposition;
        if (!string.IsNullOrEmpty(disposition)
            && ContentDispositionHeaderValue.TryParse(disposition, out ContentDispositionHeaderValue? parsed))
        {
            // FileNameStar (RFC 5987 extended form) takes precedence over the plain filename when present.
            string? name = parsed.FileNameStar.HasValue
                ? parsed.FileNameStar.Value
                : (parsed.FileName.HasValue ? parsed.FileName.Value : null);
            if (!string.IsNullOrWhiteSpace(name))
            {
                // Header values may arrive quoted ("foo.txt"); strip surrounding quotes before path-safety.
                return name.Trim('"');
            }
        }

        return null;
    }
}
