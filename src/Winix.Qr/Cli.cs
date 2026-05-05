// src/Winix.Qr/Cli.cs
#nullable enable
using System;
using System.IO;
using System.Text;
using Winix.QrCode;
using Yort.ShellKit;

namespace Winix.Qr;

/// <summary>
/// Library-level orchestration for the <c>qr</c> CLI. Program.cs is a thin shim that resolves
/// real console state (TTY-ness, stdin redirection) and delegates here. Every dispatch path —
/// arg parsing, payload materialisation, format selection, error-envelope shaping, file emission —
/// is testable from <c>Winix.Qr.Tests</c>.
/// </summary>
/// <remarks>
/// Round-1 review (CR-I1, TA-C1, TA-C2, TA-C3): prior to extraction, ~110 LOC of Program.cs
/// orchestration was untestable. The seam here mirrors the digest/notify/url/timeit/ids/when
/// pattern adopted across the suite.
/// </remarks>
public static class Cli
{
    internal const int RuntimeErrorExit = 126;

    /// <summary>
    /// Entry point. Parses <paramref name="args"/>, builds the payload, dispatches to the chosen renderer,
    /// and writes either to <paramref name="stdout"/> or to <c>OutputPath</c>.
    /// </summary>
    /// <param name="args">Raw argv (excluding the binary name).</param>
    /// <param name="stdin">Stdin reader (used only when text-mode reads stdin).</param>
    /// <param name="stdout">Where rendered text output goes when <c>OutputPath</c> is null. PNG bytes
    /// always go to <paramref name="stdoutBinary"/> (or to disk if <c>OutputPath</c> is set).</param>
    /// <param name="stderr">Where error envelopes go.</param>
    /// <param name="stdoutBinary">Raw byte sink for PNG emission to stdout (Console.OpenStandardOutput()
    /// at the real-process boundary). May be null in tests that don't exercise PNG-to-stdout.</param>
    /// <param name="stdinIsRedirected">True when stdin is piped (i.e. safe to read).</param>
    /// <param name="stdoutIsTty">True when stdout is a real terminal (drives PNG-to-TTY refusal and
    /// auto-format resolution).</param>
    /// <returns>Exit code: 0 success, 125 usage error, 126 runtime error.</returns>
    public static int Run(
        string[] args,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        Stream? stdoutBinary,
        bool stdinIsRedirected,
        bool stdoutIsTty)
    {
        ArgParser.Result r = ArgParser.Parse(args);

        // ShellKit auto-writes --help/--version/--describe output during Parse and sets IsHandled.
        if (r.IsHandled)
        {
            return r.ExitCode;
        }

        if (r.Error is not null)
        {
            stderr.WriteLine(Formatting.UsageError(r.Error));
            return ExitCode.UsageError;
        }

        QrOptions opts = r.Options!;

        // Materialise the text payload: positional > stdin > error.
        if (opts.SubCommand == SubCommand.Text)
        {
            string? textPayload = opts.TextPayload;
            if (textPayload is null && r.ReadStdin)
            {
                // If stdin is a TTY and we have no positional, that's an empty-payload error —
                // don't block waiting for input that isn't coming.
                if (stdinIsRedirected)
                {
                    try
                    {
                        textPayload = stdin.ReadToEnd().TrimEnd('\n', '\r');
                    }
                    catch (DecoderFallbackException)
                    {
                        // Reachable only when the host installs DecoderExceptionFallback on stdin.
                        // The default UTF-8 stream uses a replacement fallback; the catch is kept
                        // for hosts that opt into strict decoding. Either way, surface a clean envelope.
                        stderr.WriteLine(Formatting.RuntimeError("stdin is not valid UTF-8"));
                        return RuntimeErrorExit;
                    }
                }
            }

            if (string.IsNullOrEmpty(textPayload))
            {
                stderr.WriteLine(Formatting.UsageError("payload is empty"));
                return ExitCode.UsageError;
            }
            opts = opts with { TextPayload = textPayload };
        }

        // Round-1 review SFH-I1: --format X with --output PATH.Y where Y disagrees with X (e.g.
        // `--format svg --output code.png`) was silently writing SVG bytes to a .png file.
        // Reject explicitly so downstream tools that route on extension don't get garbage.
        string? extensionMismatch = DetectFormatExtensionMismatch(opts);
        if (extensionMismatch is not null)
        {
            stderr.WriteLine(Formatting.UsageError(extensionMismatch));
            return ExitCode.UsageError;
        }

        // Round-2 review DOCS-I2: --force is meaningful only with --output. Pre-fix the tool
        // silently accepted '--force' on its own — users assumed it had done something. Reject
        // explicitly so the contract documented in README/man (--force gates --output overwrite)
        // matches behaviour.
        if (opts.ForceOverwrite && opts.OutputPath is null)
        {
            stderr.WriteLine(Formatting.UsageError(
                "--force has no effect without --output (it gates overwriting an existing output file)."));
            return ExitCode.UsageError;
        }

        // Round-1 review SFH-I2: --output overwrote existing files silently. Without --force,
        // refuse-on-exists. The user's existing file is preserved unless they opt in.
        if (opts.OutputPath is not null && !opts.ForceOverwrite && File.Exists(opts.OutputPath))
        {
            stderr.WriteLine(Formatting.UsageError(
                $"refusing to overwrite existing file '{opts.OutputPath}' (use --force to overwrite)"));
            return ExitCode.UsageError;
        }

        // Build the payload that goes into QrEncoder/PngRenderer.
        string payload;
        try
        {
            payload = PayloadBuilder.Build(opts);
        }
        catch (InvalidOperationException ex)
        {
            stderr.WriteLine(Formatting.UsageError(ex.Message));
            return ExitCode.UsageError;
        }
        catch (ArgumentException ex)
        {
            // Round-1 review TA-I5: PayloadBuilder.Build throws ArgumentException for bad flag VALUES
            // (e.g. --security xyz, lat/lon out-of-range slipping past the parse-time validator).
            // Bad flag values are usage errors (125), not runtime errors (126). Pre-fix this routed to 126.
            stderr.WriteLine(Formatting.UsageError(ex.Message));
            return ExitCode.UsageError;
        }

        // Refuse binary-to-TTY unless explicitly forced.
        if (opts.Format == OutputFormat.Png && opts.OutputPath is null && stdoutIsTty && !opts.ForceBinary)
        {
            stderr.WriteLine(Formatting.UsageError(
                "refusing to write PNG to terminal (use --output or --force-binary)"));
            return ExitCode.UsageError;
        }

        // Dispatch to the matching renderer.
        OutputDispatcher.Result rendered;
        try
        {
            rendered = OutputDispatcher.Dispatch(payload, opts, stdoutIsTty);
        }
        catch (QrCapacityExceededException)
        {
            stderr.WriteLine(Formatting.CapacityExceededHint(opts.Ecc.ToString()));
            return RuntimeErrorExit;
        }
        // Round-1 review CR-I2 / CR-I3: previously a bare `catch (Exception)` mapped to exit code 1
        // (undocumented, off-spec — the tool advertises 0/125/126 only) AND leaked raw .Message text
        // from upstream encoder internals. Narrowed to the specific exception classes the dispatcher
        // can plausibly throw; truly unexpected exceptions now bubble to the runtime so the user
        // sees the real failure surface rather than a sanitised-but-misleading "error: …" line.
        catch (InvalidOperationException ex)
        {
            stderr.WriteLine(Formatting.RuntimeError($"render failed: {ex.Message}"));
            return RuntimeErrorExit;
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine(Formatting.RuntimeError($"render failed: {ex.Message}"));
            return RuntimeErrorExit;
        }

        // Emit.
        try
        {
            if (rendered.Bytes is not null)
            {
                if (opts.OutputPath is not null)
                {
                    File.WriteAllBytes(opts.OutputPath, rendered.Bytes);
                }
                else if (stdoutBinary is not null)
                {
                    stdoutBinary.Write(rendered.Bytes, 0, rendered.Bytes.Length);
                }
                else
                {
                    // No binary sink supplied; this is a test-time configuration error — the renderer
                    // produced bytes but Cli.Run was called without a Stream to write them to. Fail
                    // closed rather than silently dropping the QR.
                    stderr.WriteLine(Formatting.RuntimeError(
                        "no binary stdout sink configured for PNG emission"));
                    return RuntimeErrorExit;
                }
            }
            else
            {
                string text = rendered.Text!;
                if (opts.OutputPath is not null)
                {
                    File.WriteAllText(opts.OutputPath, text);
                }
                else
                {
                    stdout.Write(text);
                }
            }
        }
        // Round-2 review SFH2-I1: under InvariantGlobalization=true (qr.csproj sets this),
        // ex.Message for built-in I/O exceptions returns the raw resource KEY ('IO_PathNotFound_Path',
        // 'UnauthorizedAccess_IODenied_Path') instead of a localised English message. Classify by
        // exception subtype and emit project-controlled English text. Reserve ex.Message as a
        // fallback for genuinely unexpected I/O codes — but format it safely.
        catch (DirectoryNotFoundException)
        {
            stderr.WriteLine(Formatting.RuntimeError(
                $"write failed: parent directory does not exist: '{opts.OutputPath}'"));
            return RuntimeErrorExit;
        }
        catch (UnauthorizedAccessException)
        {
            stderr.WriteLine(Formatting.RuntimeError(
                $"write failed: permission denied: '{opts.OutputPath}'"));
            return RuntimeErrorExit;
        }
        catch (PathTooLongException)
        {
            stderr.WriteLine(Formatting.RuntimeError(
                $"write failed: path too long: '{opts.OutputPath}'"));
            return RuntimeErrorExit;
        }
        catch (IOException ex)
        {
            // Generic I/O fallback — disk full, file locked, etc. ex.Message under
            // InvariantGlobalization may still be a resource key, so prefix with our own context
            // and let the user file an issue if they can't act on the SR token.
            stderr.WriteLine(Formatting.RuntimeError(
                $"write failed for '{opts.OutputPath}' ({ex.GetType().Name}): {ex.Message}"));
            return RuntimeErrorExit;
        }

        return ExitCode.Success;
    }

    // SFH-I1: detect explicit-format-vs-extension contradiction. Returns null when the combination
    // is consistent (or there's no contradiction to detect).
    internal static string? DetectFormatExtensionMismatch(QrOptions opts)
    {
        if (opts.Format == OutputFormat.Auto || string.IsNullOrEmpty(opts.OutputPath))
        {
            return null;
        }

        string ext = Path.GetExtension(opts.OutputPath);
        if (string.IsNullOrEmpty(ext)) return null;

        OutputFormat expectedFromExt = ext.ToLowerInvariant() switch
        {
            ".png" => OutputFormat.Png,
            ".svg" => OutputFormat.Svg,
            _ => OutputFormat.Auto, // unknown extension — no contradiction to flag
        };

        if (expectedFromExt == OutputFormat.Auto) return null;
        if (expectedFromExt == opts.Format) return null;

        return $"--format {opts.Format.ToString().ToLowerInvariant()} contradicts --output extension '{ext}'. " +
               $"Use a matching extension or drop --format to auto-detect.";
    }
}
