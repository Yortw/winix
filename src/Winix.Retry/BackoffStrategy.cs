namespace Winix.Retry;

/// <summary>
/// Delay scaling strategy between retry attempts.
/// </summary>
public enum BackoffStrategy
{
    /// <summary>Same delay every time.</summary>
    Fixed,

    /// <summary>Delay grows linearly: base × attempt.</summary>
    Linear,

    /// <summary>Delay doubles each time: base × 2^(attempt−1).</summary>
    Exponential
}
