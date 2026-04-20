#nullable enable
namespace Winix.QrCode;

/// <summary>
/// Immutable QR code module matrix. Independent of any QR library — renderers take this shape only.
/// </summary>
/// <remarks>
/// <para>The module grid does NOT include the quiet zone; renderers decide whether to emit margin whitespace.</para>
/// <para><see cref="Modules"/> is indexed [row, col] with true = dark (printed) and false = light (background).</para>
/// </remarks>
public sealed class QrMatrix
{
    /// <summary>Create a matrix. <paramref name="modules"/> must be a square 2-D array.</summary>
    public QrMatrix(bool[,] modules)
    {
        if (modules.GetLength(0) != modules.GetLength(1))
        {
            throw new ArgumentException("Module matrix must be square.", nameof(modules));
        }
        Modules = modules;
        Size = modules.GetLength(0);
    }

    /// <summary>The module matrix. [row, col]. true = dark.</summary>
    public bool[,] Modules { get; }

    /// <summary>Number of modules per side. 21 for version 1, up to 177 for version 40.</summary>
    public int Size { get; }
}
