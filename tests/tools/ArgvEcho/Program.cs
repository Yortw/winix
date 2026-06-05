namespace ArgvEcho;

/// <summary>
/// Test oracle: prints each parsed argv element on its own line. Used by
/// Yort.ShellKit.Tests to pin RawCommandLineTokenizer to the .NET runtime's
/// actual command-line splitting. Vectors must not contain newlines.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        foreach (string arg in args)
        {
            System.Console.WriteLine(arg);
        }

        return 0;
    }
}
