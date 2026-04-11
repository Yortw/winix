#nullable enable

using System;
using System.Diagnostics;
using System.IO;

namespace Winix.Man;

/// <summary>
/// Selects and invokes the best available pager for displaying man page content.
/// </summary>
/// <remarks>
/// <para>
/// Resolution order:
/// <list type="number">
///   <item><description><c>$MANPAGER</c> environment variable</description></item>
///   <item><description><c>$PAGER</c> environment variable</description></item>
///   <item><description>Sibling <c>less</c>/<c>less.exe</c> in the same directory as the man binary</description></item>
///   <item><description>System <c>less</c> found on <c>PATH</c></description></item>
///   <item><description>Built-in pager (final fallback)</description></item>
/// </list>
/// </para>
/// <para>
/// When stdout is not a terminal (e.g. output is piped), content is written directly to
/// <paramref name="stdout"/> and no pager is invoked.
/// </para>
/// </remarks>
public sealed class PagerChain
{
    private readonly bool _isTerminal;
    private readonly string _exeDirectory;

    /// <summary>
    /// Initialises a new <see cref="PagerChain"/>.
    /// </summary>
    /// <param name="isTerminal">
    /// <see langword="true"/> when stdout is connected to an interactive terminal;
    /// <see langword="false"/> when output is being piped or redirected.
    /// When <see langword="false"/>, content is written directly to <paramref name="stdout"/>
    /// without invoking any pager.
    /// </param>
    /// <param name="exeDirectory">
    /// The directory containing the running man binary. Used to locate a sibling
    /// <c>less</c>/<c>less.exe</c> before falling back to the system PATH.
    /// </param>
    public PagerChain(bool isTerminal, string exeDirectory)
    {
        _isTerminal = isTerminal;
        _exeDirectory = exeDirectory ?? throw new ArgumentNullException(nameof(exeDirectory));
    }

    /// <summary>
    /// Displays <paramref name="content"/> using the highest-priority available pager.
    /// </summary>
    /// <param name="content">The rendered man page text (may contain ANSI escape codes).</param>
    /// <param name="stdout">
    /// The writer to use when stdout is not a terminal, or as a last-resort fallback
    /// if an external pager process fails to start.
    /// </param>
    public void Page(string content, TextWriter stdout)
    {
        if (stdout == null) throw new ArgumentNullException(nameof(stdout));

        // When output is piped, skip the pager entirely — the consumer handles display.
        if (!_isTerminal)
        {
            stdout.Write(content);
            return;
        }

        // Try each external pager candidate in priority order.
        string? externalPager = ResolveExternalPager();
        if (externalPager is not null)
        {
            if (TryRunExternalPager(externalPager, content))
            {
                return;
            }

            // External pager failed to launch — fall through to built-in.
        }

        // Final fallback: built-in minimal pager.
        var builtIn = new BuiltInPager();
        builtIn.Display(content);
    }

    /// <summary>
    /// Resolves the external pager executable path/command using the priority order:
    /// $MANPAGER → $PAGER → sibling less → system less.
    /// </summary>
    /// <returns>
    /// The pager executable or command string, or <see langword="null"/> if no external
    /// pager is available.
    /// </returns>
    private string? ResolveExternalPager()
    {
        // 1. $MANPAGER takes highest precedence (man-specific override).
        string? manPager = Environment.GetEnvironmentVariable("MANPAGER");
        if (!string.IsNullOrWhiteSpace(manPager))
        {
            return manPager;
        }

        // 2. $PAGER — general pager preference.
        string? pager = Environment.GetEnvironmentVariable("PAGER");
        if (!string.IsNullOrWhiteSpace(pager))
        {
            return pager;
        }

        // 3. Sibling less/less.exe in the same directory as the man binary.
        string siblingLess = Path.Combine(_exeDirectory, "less");
        if (File.Exists(siblingLess))
        {
            return siblingLess;
        }

        string siblingLessExe = Path.Combine(_exeDirectory, "less.exe");
        if (File.Exists(siblingLessExe))
        {
            return siblingLessExe;
        }

        // 4. System less on PATH.
        if (IsOnPath("less"))
        {
            return "less";
        }

        return null;
    }

    /// <summary>
    /// Attempts to run the specified external pager, writing <paramref name="content"/>
    /// to its standard input.
    /// </summary>
    /// <param name="pagerCommand">
    /// The pager executable path or command name. May be a bare name (resolved via PATH)
    /// or an absolute path.
    /// </param>
    /// <param name="content">The content to pipe into the pager's stdin.</param>
    /// <returns>
    /// <see langword="true"/> if the pager process started and exited normally;
    /// <see langword="false"/> if the process could not be started.
    /// </returns>
    private static bool TryRunExternalPager(string pagerCommand, string content)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = pagerCommand,
                UseShellExecute = false,
                RedirectStandardInput = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            // Write content and close stdin to signal EOF to the pager.
            process.StandardInput.Write(content);
            process.StandardInput.Close();

            process.WaitForExit();
            return true;
        }
        catch (Exception)
        {
            // Process.Start can throw if the executable is not found, access is denied,
            // or the path is malformed — treat any failure as "pager unavailable".
            return false;
        }
    }

    /// <summary>
    /// Checks whether <paramref name="command"/> can be found in any directory listed
    /// on the <c>PATH</c> environment variable.
    /// </summary>
    /// <remarks>
    /// On Windows, both the bare name and the name with a <c>.exe</c> extension are checked.
    /// </remarks>
    /// <param name="command">The command name to search for (without path).</param>
    /// <returns>
    /// <see langword="true"/> if the command exists in at least one PATH directory;
    /// <see langword="false"/> otherwise.
    /// </returns>
    internal static bool IsOnPath(string command)
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv is null)
        {
            return false;
        }

        foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = dir.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            string candidate = Path.Combine(trimmed, command);
            if (File.Exists(candidate))
            {
                return true;
            }

            // On Windows, also try with .exe extension.
            if (OperatingSystem.IsWindows())
            {
                string candidateExe = Path.Combine(trimmed, command + ".exe");
                if (File.Exists(candidateExe))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
