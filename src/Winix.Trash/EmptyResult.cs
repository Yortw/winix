namespace Winix.Trash;

/// <summary>Outcome of emptying the trash.</summary>
/// <param name="ItemsRemoved">Number of top-level items permanently removed (approximate — see README).</param>
public sealed record EmptyResult(int ItemsRemoved);
