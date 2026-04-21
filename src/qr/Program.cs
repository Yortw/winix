// src/qr/Program.cs
#nullable enable
using System;
using System.IO;
using System.Text;
using Winix.Qr;
using Winix.QrCode;
using Yort.ShellKit;

namespace Qr;

internal sealed class Program
{
    private const int RuntimeErrorExit = 126;

    static int Main(string[] args)
    {
        ConsoleEnv.EnableAnsiIfNeeded();
        ConsoleEnv.UseUtf8Streams();

        ArgParser.Result r = ArgParser.Parse(args);

        // ShellKit auto-writes --help/--version/--describe output during Parse and sets IsHandled.
        if (r.IsHandled)
        {
            return r.ExitCode;
        }

        if (r.Error is not null)
        {
            Console.Error.WriteLine(Formatting.UsageError(r.Error));
            return ExitCode.UsageError;
        }

        QrOptions opts = r.Options!;

        // Materialise the text payload: positional > stdin > error.
        string? textPayload = opts.TextPayload;
        if (opts.SubCommand == SubCommand.Text)
        {
            if (textPayload is null && r.ReadStdin)
            {
                // If stdin is a TTY and we have no positional, that's an empty-payload error —
                // don't block waiting for input that isn't coming.
                if (Console.IsInputRedirected)
                {
                    try
                    {
                        textPayload = Console.In.ReadToEnd().TrimEnd('\n', '\r');
                    }
                    catch (DecoderFallbackException)
                    {
                        Console.Error.WriteLine(Formatting.RuntimeError("stdin is not valid UTF-8"));
                        return RuntimeErrorExit;
                    }
                }
            }

            if (string.IsNullOrEmpty(textPayload))
            {
                Console.Error.WriteLine(Formatting.UsageError("payload is empty"));
                return ExitCode.UsageError;
            }
            opts = opts with { TextPayload = textPayload };
        }

        // Build the payload that goes into QrEncoder/PngRenderer.
        string payload;
        try
        {
            payload = PayloadBuilder.Build(opts);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(Formatting.UsageError(ex.Message));
            return ExitCode.UsageError;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(Formatting.RuntimeError(ex.Message));
            return RuntimeErrorExit;
        }

        // Refuse binary-to-TTY unless explicitly forced.
        bool stdoutIsTty = !Console.IsOutputRedirected;
        if (opts.Format == OutputFormat.Png && opts.OutputPath is null && stdoutIsTty && !opts.ForceBinary)
        {
            Console.Error.WriteLine(Formatting.UsageError(
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
            Console.Error.WriteLine(Formatting.CapacityExceededHint(opts.Ecc.ToString()));
            return RuntimeErrorExit;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(Formatting.RuntimeError($"error: {ex.Message}"));
            return 1;
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
                else
                {
                    using Stream stdout = Console.OpenStandardOutput();
                    stdout.Write(rendered.Bytes, 0, rendered.Bytes.Length);
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
                    Console.Out.Write(text);
                }
            }
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine(Formatting.RuntimeError($"write failed: {ex.Message}"));
            return RuntimeErrorExit;
        }

        return ExitCode.Success;
    }

}
