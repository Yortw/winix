#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace Winix.Online;

/// <summary>A single network-readiness check evaluated once per poll cycle.</summary>
public interface IReadinessCheck
{
    /// <summary>Evaluates the check once. Honours <paramref name="cancellationToken"/> for user cancel.</summary>
    Task<CheckResult> RunAsync(CancellationToken cancellationToken);
}
