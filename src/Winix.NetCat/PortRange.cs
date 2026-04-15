#nullable enable

using System;
using System.Collections.Generic;

namespace Winix.NetCat;

/// <summary>
/// Inclusive range of TCP/UDP port numbers (Low ≤ High). A single port is represented
/// as a range where Low equals High.
/// </summary>
public readonly struct PortRange : IEquatable<PortRange>
{
    /// <summary>The lowest port in the range (1-65535).</summary>
    public int Low { get; }

    /// <summary>The highest port in the range (1-65535, ≥ <see cref="Low"/>).</summary>
    public int High { get; }

    /// <summary>The number of ports in the range (always ≥ 1).</summary>
    public int Count => High - Low + 1;

    /// <summary>
    /// Constructs a range. Throws <see cref="ArgumentOutOfRangeException"/> if
    /// <paramref name="low"/> or <paramref name="high"/> is outside 1-65535,
    /// or if <paramref name="low"/> &gt; <paramref name="high"/>.
    /// </summary>
    public PortRange(int low, int high)
    {
        if (low < 1 || low > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(low), "Port must be 1-65535.");
        }
        if (high < 1 || high > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(high), "Port must be 1-65535.");
        }
        if (low > high)
        {
            throw new ArgumentOutOfRangeException(nameof(low), "Low port must not exceed high port.");
        }
        Low = low;
        High = high;
    }

    /// <summary>Constructs a single-port range.</summary>
    public PortRange(int port) : this(port, port) { }

    /// <summary>Yields each port in the range, ascending.</summary>
    public IEnumerable<int> Enumerate()
    {
        for (int port = Low; port <= High; port++)
        {
            yield return port;
        }
    }

    /// <inheritdoc />
    public bool Equals(PortRange other) => Low == other.Low && High == other.High;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PortRange other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Low, High);

    /// <summary>Returns "PORT" for single ports, "LOW-HIGH" otherwise.</summary>
    public override string ToString() => Low == High ? Low.ToString() : $"{Low}-{High}";
}
