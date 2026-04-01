using System.Globalization;
using Winix.FileWalk;
using Yort.ShellKit;

namespace Winix.TreeX;

/// <summary>
/// Walks a <see cref="TreeNode"/> tree and produces formatted text output with
/// Unicode box-drawing connectors, optional ANSI colour, OSC 8 hyperlinks,
/// and optional size/date columns.
/// </summary>
public sealed class TreeRenderer
{
    // Box-drawing connectors
    private const string TJunction = "\u251C\u2500\u2500 ";   // ├──
    private const string Elbow = "\u2514\u2500\u2500 ";        // └──
    private const string VerticalBar = "\u2502   ";            // │   (vertical + 3 spaces)
    private const string Blank = "    ";                       // 4 spaces (last-child continuation)

    private readonly TreeRenderOptions _options;

    /// <summary>
    /// Initialises a new <see cref="TreeRenderer"/> with the given display options.
    /// </summary>
    /// <param name="options">Controls colour, links, size/date columns, and directory-only mode.</param>
    public TreeRenderer(TreeRenderOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Renders the tree rooted at <paramref name="root"/> to <paramref name="writer"/>,
    /// returning summary statistics about the rendered entries.
    /// </summary>
    /// <param name="root">The root node of the tree to render.</param>
    /// <param name="writer">The text writer to emit formatted lines to.</param>
    /// <returns>A <see cref="TreeStats"/> summarising directories, files, and total size.</returns>
    public TreeStats Render(TreeNode root, TextWriter writer)
    {
        int maxSizeWidth = 0;
        if (_options.ShowSize)
        {
            maxSizeWidth = ComputeMaxSizeWidth(root);
        }

        // Root line: name with optional colour
        writer.Write(FormatName(root));
        writer.WriteLine();

        int dirCount = 0;
        int fileCount = 0;
        long totalSize = 0;

        RenderChildren(root.Children, writer, "", maxSizeWidth, ref dirCount, ref fileCount, ref totalSize);

        return new TreeStats(dirCount, fileCount, totalSize);
    }

    /// <summary>
    /// Recursively renders child nodes with proper tree-line indentation.
    /// </summary>
    private void RenderChildren(
        List<TreeNode> children,
        TextWriter writer,
        string prefix,
        int maxSizeWidth,
        ref int dirCount,
        ref int fileCount,
        ref long totalSize)
    {
        // Build list of renderable children (respects DirsOnly)
        List<TreeNode> visible = GetVisibleChildren(children);

        for (int i = 0; i < visible.Count; i++)
        {
            TreeNode child = visible[i];
            bool isLast = (i == visible.Count - 1);

            string connector = isLast ? Elbow : TJunction;
            string childPrefix = isLast ? Blank : VerticalBar;

            // Track stats
            if (child.Type == FileEntryType.Directory)
            {
                dirCount++;
            }
            else
            {
                fileCount++;
                totalSize += child.SizeBytes > 0 ? child.SizeBytes : 0;
            }

            // Build the line
            writer.Write(prefix);
            WriteConnector(writer, connector);
            writer.Write(FormatName(child));

            if (_options.ShowSize)
            {
                WriteSizeColumn(writer, child, maxSizeWidth);
            }

            if (_options.ShowDate)
            {
                WriteDateColumn(writer, child);
            }

            writer.WriteLine();

            // Recurse into directory children
            if (child.Type == FileEntryType.Directory && child.Children.Count > 0)
            {
                RenderChildren(
                    child.Children,
                    writer,
                    prefix + childPrefix,
                    maxSizeWidth,
                    ref dirCount,
                    ref fileCount,
                    ref totalSize);
            }
        }
    }

    /// <summary>
    /// Returns the list of children that should be rendered, filtering out
    /// files when <see cref="TreeRenderOptions.DirsOnly"/> is set.
    /// </summary>
    private List<TreeNode> GetVisibleChildren(List<TreeNode> children)
    {
        if (!_options.DirsOnly)
        {
            return children;
        }

        var result = new List<TreeNode>();
        foreach (TreeNode child in children)
        {
            if (child.Type == FileEntryType.Directory)
            {
                result.Add(child);
            }
        }

        return result;
    }

    /// <summary>
    /// Writes a tree connector (├──, └──) with optional dim colour.
    /// </summary>
    private void WriteConnector(TextWriter writer, string connector)
    {
        writer.Write(AnsiColor.Dim(_options.UseColor));
        writer.Write(connector);
        writer.Write(AnsiColor.Reset(_options.UseColor));
    }

    /// <summary>
    /// Formats a node name with optional colour and OSC 8 hyperlink.
    /// Colour wraps the link, which wraps the text.
    /// </summary>
    private string FormatName(TreeNode node)
    {
        string name = node.Name;

        // Wrap name in OSC 8 link if enabled
        if (_options.UseLinks)
        {
            string url = PathToFileUrl(node.FullPath);
            name = $"\x1b]8;;{url}\x1b\\{name}\x1b]8;;\x1b\\";
        }

        // Wrap in colour based on type
        string colorStart = GetNodeColor(node);
        string colorEnd = colorStart.Length > 0 ? AnsiColor.Reset(_options.UseColor) : "";

        return $"{colorStart}{name}{colorEnd}";
    }

    /// <summary>
    /// Returns the ANSI colour escape for a node based on its type,
    /// or empty string if colour is disabled or no colour applies.
    /// </summary>
    private string GetNodeColor(TreeNode node)
    {
        if (!_options.UseColor)
        {
            return "";
        }

        if (node.Type == FileEntryType.Directory)
        {
            return AnsiColor.Blue(true);
        }

        if (node.Type == FileEntryType.Symlink)
        {
            return AnsiColor.Cyan(true);
        }

        if (node.IsExecutable)
        {
            return AnsiColor.Green(true);
        }

        return "";
    }

    /// <summary>
    /// Writes the right-aligned size column for a node.
    /// </summary>
    private void WriteSizeColumn(TextWriter writer, TreeNode node, int maxSizeWidth)
    {
        writer.Write("  ");
        writer.Write(AnsiColor.Dim(_options.UseColor));
        writer.Write(HumanSize.FormatPadded(node.SizeBytes, maxSizeWidth));
        writer.Write(AnsiColor.Reset(_options.UseColor));
    }

    /// <summary>
    /// Writes the date column for a node.
    /// </summary>
    private void WriteDateColumn(TextWriter writer, TreeNode node)
    {
        writer.Write("  ");
        writer.Write(AnsiColor.Dim(_options.UseColor));
        writer.Write(node.Modified.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        writer.Write(AnsiColor.Reset(_options.UseColor));
    }

    /// <summary>
    /// Computes the maximum width of formatted size strings across all nodes in the tree,
    /// used to right-align the size column.
    /// </summary>
    private static int ComputeMaxSizeWidth(TreeNode node)
    {
        int max = HumanSize.Format(node.SizeBytes).Length;
        ComputeMaxSizeWidthRecursive(node, ref max);
        return max;
    }

    /// <summary>
    /// Recursively walks children to find the widest size string.
    /// </summary>
    private static void ComputeMaxSizeWidthRecursive(TreeNode node, ref int max)
    {
        foreach (TreeNode child in node.Children)
        {
            int len = HumanSize.Format(child.SizeBytes).Length;
            if (len > max)
            {
                max = len;
            }

            ComputeMaxSizeWidthRecursive(child, ref max);
        }
    }

    /// <summary>
    /// Converts an absolute filesystem path to a properly encoded file:// URL.
    /// Uses <see cref="Uri"/> to correctly percent-encode spaces, #, ?, Unicode, and
    /// other characters that are invalid in URLs.
    /// </summary>
    private static string PathToFileUrl(string fullPath)
    {
        return new Uri(fullPath).AbsoluteUri;
    }
}
