namespace Yort.ShellKit;

/// <summary>
/// Helpers for unwrapping CLR exception wrappers that obscure the actionable inner cause.
/// </summary>
/// <remarks>
/// Consolidated from per-tool copies (wargs, retry, nc, envvault) so every Winix tool
/// surfaces the real cause of a failed static constructor / native P/Invoke cctor instead
/// of the framework's useless "The type initializer for X threw an exception." wrapper text.
/// </remarks>
public static class ExceptionUnwrap
{
    // Maximum unwrap depth before the loop bails out. Pathological cases (cyclic type-init
    // or generic-instantiation chains) exceeding this are extremely unlikely; the cap exists
    // to guarantee the helper terminates regardless of the input shape. Kept private — no
    // caller references it (the tests assert the cap via behaviour at 31/33), so it is an
    // implementation detail, not part of ShellKit's public surface.
    private const int MaxDepth = 32;

    /// <summary>
    /// Peels <see cref="System.TypeInitializationException"/> wrappers to reveal the
    /// actionable inner exception, discarding the depth-cap signal. Use this overload when the
    /// caller only needs the surfaced cause for a one-line error message.
    /// </summary>
    /// <param name="ex">Exception to unwrap. Returned unchanged if not a TIE.</param>
    /// <returns>The innermost exception after unwrapping.</returns>
    public static System.Exception UnwrapTypeInit(System.Exception ex) => UnwrapTypeInitWithDepth(ex).Surface;

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
    public static (System.Exception Surface, bool DepthCapped) UnwrapTypeInitWithDepth(System.Exception ex)
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
