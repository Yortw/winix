using Winix.Peep;
using Xunit;

namespace Winix.Peep.Tests;

/// <summary>
/// Pins the output-cap behaviour added to <see cref="CommandExecutor"/>: a runaway
/// child must be capped to the <c>maxOutputChars</c> argument with a truncation
/// marker rather than growing the StringBuilder unbounded and OOM'ing the peep
/// process. Cap is now a per-call argument (no shared static state) so parallel
/// xUnit cases can't race on the cap value or leak test-overridden numbers into
/// production-style error messages.
/// </summary>
public class CommandExecutorOutputCapTests
{
    [Fact]
    public async Task RunAsync_ChildOutputExceedsCap_TruncatesAndAppendsMarker()
    {
        // Use a small cap so we don't have to generate megabytes of output.
        const int cap = 256;

        // Use a portable shell idiom to print 50 lines of ~20 chars each => ~1000 chars,
        // well over the 256 cap. We use the host shell because cross-platform "print
        // many lines" requires either cmd /c or sh -c.
        string command;
        string[] arguments;
        if (OperatingSystem.IsWindows())
        {
            command = "cmd.exe";
            arguments = new[] { "/c", "for /L %i in (1,1,50) do @echo padding-line-content-XX-%i" };
        }
        else
        {
            command = "/bin/sh";
            arguments = new[] { "-c", "for i in $(seq 1 50); do echo padding-line-content-XX-$i; done" };
        }

        var result = await CommandExecutor.RunAsync(
            command, arguments, TriggerSource.Initial, cancellationToken: default, maxOutputChars: cap);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("[peep: output truncated at", result.Output);
        Assert.Contains($"{cap} characters", result.Output);
        // The captured output should be approximately the cap size plus the marker line.
        // Allow some slack because ReadStreamAsync only checks the cap per chunk, so the
        // last chunk may push slightly past the cap before triggering. Marker is ~80 chars.
        Assert.InRange(result.Output.Length, cap, cap + 4096 + 200);
    }

    [Fact]
    public async Task RunAsync_ChildOutputUnderCap_NoTruncationMarker()
    {
        const int cap = 1024;

        string command;
        string[] arguments;
        if (OperatingSystem.IsWindows())
        {
            command = "cmd.exe";
            arguments = new[] { "/c", "echo small output" };
        }
        else
        {
            command = "/bin/sh";
            arguments = new[] { "-c", "echo small output" };
        }

        var result = await CommandExecutor.RunAsync(
            command, arguments, TriggerSource.Initial, cancellationToken: default, maxOutputChars: cap);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("[peep: output truncated", result.Output);
        Assert.Contains("small output", result.Output);
    }

    [Fact]
    public async Task RunAsync_NegativeMaxOutputChars_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await CommandExecutor.RunAsync(
                "echo", new[] { "x" }, TriggerSource.Initial, cancellationToken: default, maxOutputChars: -1);
        });
    }
}
