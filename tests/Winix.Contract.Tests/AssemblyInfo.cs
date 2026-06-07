using Xunit;

// ConsoleCapture swaps the process-global Console.Out around every seam invocation;
// any parallel test writing to the console during a capture window corrupts snapshots.
// (Adversarial review F1 — this attribute is load-bearing, not style.)
[assembly: CollectionBehavior(DisableTestParallelization = true)]
