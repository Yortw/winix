#nullable enable
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace Winix.HCat.Handlers;

/// <summary>Wires the serve-mode middleware: static file serving plus directory browsing, rooted at the
/// requested directory via a <see cref="PhysicalFileProvider"/>. Upload receiving and upload-dir exclusion
/// are layered on in a later task; this stage covers static files + listing only.</summary>
public static class ServeConfig
{
    /// <summary>Applies static-file + directory-browsing middleware to <paramref name="app"/>, rooted at
    /// <c>o.Directory</c>. The directory is resolved to an absolute path so the provider is stable
    /// regardless of the process working directory.</summary>
    /// <param name="app">The application to configure.</param>
    /// <param name="o">The parsed options (only <see cref="HCatOptions.Directory"/> is used here).</param>
    public static void Apply(WebApplication app, HCatOptions o)
    {
        // PhysicalFileProvider requires an absolute, rooted path; "." would throw.
        string root = Path.GetFullPath(o.Directory);
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
}
