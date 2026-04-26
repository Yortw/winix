namespace Winix.Wargs;

/// <summary>
/// Helpers for unwrapping CLR exception wrappers that obscure the actionable inner cause.
/// </summary>
/// <remarks>
/// Extracted from Program.cs in round 15 to make the depth-cap notice behaviour directly
/// testable. The same pattern exists in retry/Program.cs and envvault's Cli.UnwrapTypeInit;
/// centralising across tools is deferred to a suite-wide ShellKit follow-up.
/// </remarks>
public static class ExceptionUnwrap
{
    /// <summary>
    /// Maximum unwrap depth before the loop bails out. Pathological cases (cyclic type-init
    /// or generic-instantiation chains) exceeding this are extremely unlikely; the cap exists
    /// to guarantee the helper terminates regardless of the input shape.
    /// </summary>
    public const int MaxDepth = 32;

    /// <summary>
    /// Peels <see cref="System.TypeInitializationException"/> wrappers to reveal the
    /// actionable inner exception. The wrapper's Message is "The type initializer for X
    /// threw an exception." — useless to the user.
    /// </summary>
    /// <param name="ex">Exception to unwrap. Returned unchanged if not a TIE.</param>
    /// <returns>
    /// Tuple of (innermost exception after unwrap, depthCapped flag). depthCapped is true
    /// when the depth cap stopped the loop while a further TIE was still wrapping a real
    /// cause — callers should surface this so the user knows the displayed message may not
    /// be the genuine root cause.
    /// </returns>
    public static (System.Exception Surface, bool DepthCapped) UnwrapTypeInit(System.Exception ex)
    {
        System.Exception current = ex;
        int depth;
        for (depth = 0; depth < MaxDepth && current is System.TypeInitializationException tie && tie.InnerException != null; depth++)
        {
            current = tie.InnerException;
        }
        bool depthCapped = depth == MaxDepth && current is System.TypeInitializationException capTie && capTie.InnerException != null;
        return (current, depthCapped);
    }
}
