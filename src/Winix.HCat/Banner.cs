#nullable enable
using System.IO;
using System.Text;

namespace Winix.HCat;

/// <summary>Builds the human-readable bind banner shown when the server starts: the served-target / mode
/// line, each display URL, the loopback-only hint, the QR (when exposed), and the served-root upload
/// warning. Pure string building — no I/O — so it is fully unit-testable.</summary>
public static class Banner
{
    /// <summary>Renders the bind banner. Includes the loopback-only <c>--lan</c> hint when the bind is not
    /// <see cref="BindInfo.Exposed"/>; appends <paramref name="qr"/> when the bind is exposed and the QR is
    /// non-null; and, for serve+upload where the resolved upload directory sits within the served tree,
    /// appends a warning that uploaded files will be downloadable. When <paramref name="useColor"/> is true,
    /// the "Serving" label and loopback hint are dimmed, URLs are cyan, and the ⚠ warning is yellow;
    /// plain output (useColor false) is byte-identical to the pre-colour version.</summary>
    /// <param name="info">Resolved bind target and display URLs.</param>
    /// <param name="options">The parsed invocation options (mode, served directory, upload settings).</param>
    /// <param name="qr">A pre-rendered QR block to show when exposed, or null to omit it.</param>
    /// <param name="useColor">When true, emit ANSI colour sequences. Pass <c>options.UseColor</c> at the
    /// production call site; pass false in tests that assert on plain content.</param>
    public static string Render(BindInfo info, HCatOptions options, string? qr, bool useColor)
    {
        string dim    = Yort.ShellKit.AnsiColor.Dim(useColor);
        string cyan   = Yort.ShellKit.AnsiColor.Cyan(useColor);
        string yellow = Yort.ShellKit.AnsiColor.Yellow(useColor);
        string reset  = Yort.ShellKit.AnsiColor.Reset(useColor);

        var sb = new StringBuilder();

        sb.Append(dim).Append("Serving ").Append(reset).Append(DescribeTarget(options)).Append('\n');

        foreach (string url in info.Urls)
        {
            sb.Append("  ").Append(cyan).Append(url).Append(reset).Append('\n');
        }

        if (!info.Exposed)
        {
            sb.Append(dim).Append("(localhost only — pass --lan to share on your LAN)").Append(reset).Append('\n');
        }

        if (info.Exposed && qr != null)
        {
            sb.Append(qr).Append('\n');
        }

        if (options.Mode == HCatMode.Serve && options.Upload)
        {
            // Warn ONLY when uploads are actually downloadable — i.e. the upload dir IS the served root.
            // An in-tree-but-not-root dir (incl. the default ./uploads) is excluded from serving by
            // ServeConfig, so warning there would be false. Both sites derive from IsServedRoot so the
            // warning and the exclusion behaviour cannot diverge.
            string uploadDir = options.UploadDir ?? Path.Combine(options.Directory, "uploads");
            if (UploadPathSafety.IsServedRoot(options.Directory, uploadDir))
            {
                sb.Append(yellow).Append("⚠ uploads land in the served root and will be downloadable").Append(reset).Append('\n');
            }
        }

        return sb.ToString();
    }

    /// <summary>One-line description of what is being served, by mode.</summary>
    private static string DescribeTarget(HCatOptions options)
    {
        switch (options.Mode)
        {
            case HCatMode.Inspect:
                return "request inspector";
            case HCatMode.Pipe:
                return "command pipe";
            case HCatMode.Serve:
            default:
                return options.Directory + (options.Upload ? " (serve + upload)" : "");
        }
    }
}
