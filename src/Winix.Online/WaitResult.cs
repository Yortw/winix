#nullable enable

using System;
using System.Collections.Generic;

namespace Winix.Online;

/// <summary>
/// Outcome of a wait. <see cref="Ready"/> ⇒ exit 0. <see cref="TimedOut"/> ⇒ exit 124.
/// Neither (only possible under <c>--once</c>) ⇒ exit 1.
/// </summary>
/// <param name="Ready">Every requested check passed in the final cycle.</param>
/// <param name="TimedOut">The wait budget was exhausted before ready (wait mode only).</param>
/// <param name="Attempts">Number of poll cycles run.</param>
/// <param name="Elapsed">Wall time as measured by the injected clock.</param>
/// <param name="LastChecks">Per-check results from the final cycle (for the JSON envelope / summary).</param>
public sealed record WaitResult(
    bool Ready,
    bool TimedOut,
    int Attempts,
    TimeSpan Elapsed,
    IReadOnlyList<CheckResult> LastChecks);
