using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sandbox;

/// <summary>
/// Session-wide bot state: enable toggle, per-slot identity (name, outfit), and
/// detection util methods. Slot index (0..BotCount-1) keys the persistent
/// per-bot data so identity survives lobby ↔ game scene transitions.
/// Cleared on host restart.
/// </summary>
public static class Bots
{
    public const int BotCount = 3;

    public static bool IsEnabled { get; private set; }

    public static void SetEnabled( bool enabled ) => IsEnabled = enabled;

    // --- Detection ---------------------------------------------------------

    public static bool IsBot( this GameObject go )
    {
        if ( !go.IsValid() ) return false;
        return go.Components.Get<BotController>() != null;
    }

    public static bool IsBot( this Component component )
    {
        if ( !component.IsValid() ) return false;
        return component.GameObject.IsBot();
    }

    // --- Names -------------------------------------------------------------

    private static readonly string[] _namePool =
    [
        "Waffle", "Pixel", "Turbo", "Biscuit", "Zap",
        "Noodle", "Bumper", "Sprocket", "Wobble", "Fizz",
        "Muffin", "Rocket", "Pickle", "Doodle", "Blip",
        "Gadget", "Crumb", "Squish", "Boop", "Tumble",
    ];

    private static readonly List<string> _assignedNames = new();

    /// <summary>
    /// Name for the given bot slot. First access rolls a random unused name from
    /// the pool; later accesses return the cached choice.
    /// </summary>
    public static string NameForSlot( int slot )
    {
        while ( _assignedNames.Count <= slot )
        {
            List<string> remaining = _namePool.Except( _assignedNames ).ToList();
            string next = remaining.Count > 0
                ? remaining[Game.Random.Int( remaining.Count - 1 )]
                : _namePool[Game.Random.Int( _namePool.Length - 1 )];
            _assignedNames.Add( next );
        }
        return _assignedNames[slot];
    }

    // --- Outfits -----------------------------------------------------------

    private static readonly Dictionary<int, List<ClothingContainer.ClothingEntry>> _outfitCache = new();

    /// <summary>
    /// Populates and applies the outfit for the given bot slot. First access
    /// rolls and caches a random outfit; later accesses restore the cached one.
    /// </summary>
    public static async Task ApplyOutfitForSlot( Dresser dresser, int slot )
    {
        if ( _outfitCache.TryGetValue( slot, out List<ClothingContainer.ClothingEntry> cached ) )
        {
            dresser.Clothing.Clear();
            foreach ( ClothingContainer.ClothingEntry entry in cached )
                dresser.Clothing.Add( entry );
            await dresser.Apply();
            return;
        }

        dresser.Randomize();
        await dresser.Apply();
        _outfitCache[slot] = new List<ClothingContainer.ClothingEntry>( dresser.Clothing );
    }
}
