using System.Runtime.InteropServices;
using Winix.Wargs;
using Xunit;

namespace Winix.Wargs.Tests;

public class IntegrationTests
{
    [Fact]
    public async Task FullPipeline_EchoThreeItems_ProducesThreeResults()
    {
        var input = new InputReader(new StringReader("alpha\nbeta\ngamma"), DelimiterMode.Line);

        string[] template;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            template = new[] { "cmd", "/c", "echo", "{}" };
        }
        else
        {
            template = new[] { "echo", "{}" };
        }

        var builder = new CommandBuilder(template);
        var options = new JobRunnerOptions();
        var runner = new JobRunner(options);

        var stdout = new StringWriter();
        var result = await runner.RunAsync(
            builder.Build(input.ReadItems()).ToList(), stdout, TextWriter.Null);

        Assert.Equal(3, result.TotalJobs);
        Assert.Equal(3, result.Succeeded);
        Assert.Equal(0, result.Failed);

        string output = stdout.ToString();
        Assert.Contains("alpha", output);
        Assert.Contains("beta", output);
        Assert.Contains("gamma", output);
    }

    [Fact]
    public async Task FullPipeline_ParallelWithKeepOrder_OutputInOrder()
    {
        var input = new InputReader(new StringReader("1\n2\n3\n4"), DelimiterMode.Line);

        string[] template;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            template = new[] { "cmd", "/c", "echo", "{}" };
        }
        else
        {
            template = new[] { "echo", "{}" };
        }

        var builder = new CommandBuilder(template);
        var options = new JobRunnerOptions(Parallelism: 2, Strategy: BufferStrategy.KeepOrder);
        var runner = new JobRunner(options);

        var stdout = new StringWriter();
        var result = await runner.RunAsync(
            builder.Build(input.ReadItems()).ToList(), stdout, TextWriter.Null);

        Assert.Equal(4, result.Succeeded);

        string output = stdout.ToString();
        int pos1 = output.IndexOf("1");
        int pos2 = output.IndexOf("2");
        int pos3 = output.IndexOf("3");
        int pos4 = output.IndexOf("4");

        Assert.True(pos1 >= 0 && pos2 >= 0 && pos3 >= 0 && pos4 >= 0, "All items should appear in output");
        Assert.True(pos1 < pos2, "1 before 2");
        Assert.True(pos2 < pos3, "2 before 3");
        Assert.True(pos3 < pos4, "3 before 4");
    }

    [Fact]
    public void FormatJson_RoundTrips_StandardFields()
    {
        var jobs = new List<JobResult>
        {
            new(1, 0, "out\n", TimeSpan.FromSeconds(0.1), new[] { "a" }, false),
            new(2, 1, "err\n", TimeSpan.FromSeconds(0.2), new[] { "b" }, false),
        };
        var result = new WargsResult(2, 1, 1, 0, TimeSpan.FromSeconds(0.3), jobs);

        string json = Formatting.FormatJson(result, WargsExitCode.ChildFailed, "child_failed", "wargs", "0.1.0");

        Assert.Contains("\"total_jobs\":2", json);
        Assert.Contains("\"succeeded\":1", json);
        Assert.Contains("\"failed\":1", json);
        Assert.Contains("\"exit_code\":123", json);
    }
}
