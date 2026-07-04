using System.Collections.Generic;
using System.Threading.Tasks;
using Sandbox;

/// <summary>
/// Persistent per-slot outfit cache for AI bots. First spawn of a slot rolls a
/// random outfit via <see cref="Dresser.Randomize"/> and caches the resulting
/// clothing list; later spawns of the same slot restore the cached list so bots
/// keep their look across lobby ↔ game scene transitions.
/// </summary>
public static class BotOutfits
{
    private static readonly Dictionary<int, List<ClothingContainer.ClothingEntry>> _cache = new();

    /// <summary>
    /// Populates <paramref name="dresser"/>.Clothing for the given bot slot and
    /// applies it. Rolls a new random outfit on first access; otherwise restores
    /// the cached one. Awaitable so callers can sequence Network.Refresh after.
    /// </summary>
    public static async Task ApplyForSlot( Dresser dresser, int slot )
    {
        if ( _cache.TryGetValue( slot, out List<ClothingContainer.ClothingEntry> cached ) )
        {
            dresser.Clothing.Clear();
            foreach ( ClothingContainer.ClothingEntry entry in cached )
            {
                dresser.Clothing.Add( entry );
            }
            await dresser.Apply();
            return;
        }

        dresser.Randomize();
        await dresser.Apply();
        _cache[slot] = new List<ClothingContainer.ClothingEntry>( dresser.Clothing );
    }
}
