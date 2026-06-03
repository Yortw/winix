using Xunit;

// Many Less tests swap process-global Console.Out / Console.In (Console.SetOut/SetIn around the
// dump and CLI paths) to capture output. xUnit runs distinct test CLASSES in parallel by default,
// so e.g. ColourRegressionTests and Round1CoverageTests race over the single global Console.Out:
// one class's dump output lands in another class's StringWriter, intermittently failing an
// Assert.Contains ("sub-string not found"). It surfaced as a macOS-only CI flake (Windows/Linux
// passed by scheduler luck). Serialising the assembly removes the race deterministically; the
// project is small, so the lost parallelism is negligible.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
