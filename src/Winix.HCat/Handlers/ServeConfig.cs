#nullable enable
using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace Winix.HCat.Handlers;

/// <summary>Wires the serve-mode middleware: static file serving plus directory browsing, rooted at the
/// requested directory via a <see cref="PhysicalFileProvider"/>. When <c>--upload</c> is set, also registers
/// the POST upload receiver and — for an upload root that sits <em>inside</em> the served tree but is not the
/// served root itself — a download-exclusion guard that 404s GETs under the upload sub-path.</summary>
public static class ServeConfig
{
    /// <summary>Applies the serve-mode middleware to <paramref name="app"/>, rooted at <c>o.Directory</c>. The
    /// directory is resolved to an absolute path so the provider is stable regardless of the process working
    /// directory.</summary>
    /// <param name="app">The application to configure.</param>
    /// <param name="o">The parsed options (<see cref="HCatOptions.Directory"/>, <see cref="HCatOptions.Upload"/>,
    /// <see cref="HCatOptions.UploadDir"/>).</param>
    /// <remarks>
    /// F1 — middleware ORDER is load-bearing. <see cref="StaticFileExtensions.UseStaticFiles(IApplicationBuilder)"/>
    /// / <see cref="FileServerExtensions.UseFileServer(IApplicationBuilder)"/> short-circuit (terminate) on a
    /// matched file. The upload-exclusion guard MUST therefore be registered BEFORE the file-server middleware:
    /// if it ran after, an uploaded file physically inside the served tree would be served (200) before the 404
    /// guard ever executed — silently breaking the secure-by-default invariant. The exclusion <c>app.Use(...)</c>
    /// below is deliberately registered ahead of <c>UseFileServer</c>.
    /// </remarks>
    public static void Apply(WebApplication app, HCatOptions o)
    {
        // PhysicalFileProvider requires an absolute, rooted path; "." would throw.
        string root = Path.GetFullPath(o.Directory);

        if (o.Upload)
        {
            string uploadRoot = UploadHandler.ResolveUploadRoot(o);

            // Exclude downloads only when the upload root sits INSIDE the served tree but is NOT the served root.
            // When it IS the served root (--upload-dir .), uploads are inherently downloadable — the documented
            // escape hatch — so no exclusion is registered.
            bool withinTree = UploadPathSafety.IsWithinServedTree(root, uploadRoot);
            bool isServedRoot = UploadPathSafety.IsServedRoot(root, uploadRoot);
            if (withinTree && !isServedRoot)
            {
                // Map the on-disk upload sub-path to its URL prefix (relative to the served root), normalised to
                // forward slashes with a leading slash, e.g. "/uploads". GETs under this prefix are 404'd.
                string relative = Path.GetRelativePath(root, uploadRoot).Replace('\\', '/').TrimEnd('/');
                string excludePrefix = "/" + relative;

                StringComparison cmp = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;

                // F1: registered BEFORE UseFileServer so it wins over the terminal static-file short-circuit.
                app.Use(async (context, next) =>
                {
                    bool isRead = HttpMethods.IsGet(context.Request.Method)
                        || HttpMethods.IsHead(context.Request.Method);
                    if (isRead && IsUnderPrefix(context.Request.Path, excludePrefix, cmp))
                    {
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                        return;
                    }
                    await next().ConfigureAwait(false);
                });
            }

            // The POST receiver is method-gated middleware; it runs only for POST and passes non-POST requests
            // through to the file-server below, so registration order relative to UseFileServer is immaterial
            // for it (unlike the exclusion guard above, where order is the whole point).
            UploadHandler.Apply(app, o);
        }

        var provider = new PhysicalFileProvider(root);
        var options = new FileServerOptions
        {
            FileProvider = provider,
            RequestPath = string.Empty,
            EnableDirectoryBrowsing = true,
        };
        // Serve files without a registered MIME type (e.g. extensionless) as octet-stream rather than 404.
        options.StaticFileOptions.ServeUnknownFileTypes = true;
        options.StaticFileOptions.DefaultContentType = "application/octet-stream";

        app.UseFileServer(options);
    }

    /// <summary>True when the request path equals the exclude prefix or sits below it at a path boundary.
    /// A bare prefix check is unsound ("/uploads" would also match "/uploads-public"), so a boundary segment
    /// is required.</summary>
    private static bool IsUnderPrefix(PathString path, string excludePrefix, StringComparison cmp)
    {
        string value = path.HasValue ? path.Value! : "/";
        return value.Equals(excludePrefix, cmp)
            || value.StartsWith(excludePrefix + "/", cmp);
    }
}
