namespace Winix.Ids;

/// <summary>
/// Generator for a single identifier type. Implementations may hold internal state
/// (for monotonicity) but the interface contract is stateless from the caller's view.
/// </summary>
public interface IIdGenerator
{
    /// <summary>Produces one identifier string shaped by <paramref name="options"/>.</summary>
    string Generate(IdsOptions options);
}
