using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sandbox;

/// <summary>
/// Session-wide bot state: desired-count, per-slot identity (name, outfit), and
/// detection util methods. Slot index (0..DesiredCount-1) keys the persistent
/// per-bot data so identity survives lobby ↔ game scene transitions.
/// Cleared on host restart.
/// </summary>
public static class Bots
{
    public const int MaxCount = 30;

    /// <summary>How many bots the host wants live. Callers change this via <see cref="ChangeCount"/>.</summary>
    public static int DesiredCount { get; private set; }

    /// <summary>Overwrite the count (used by the RPC that fans a change to every client).</summary>
    public static void SetDesiredCount( int count ) => DesiredCount = Math.Clamp( count, 0, MaxCount );

    /// <summary>Bump <see cref="DesiredCount"/> by <paramref name="delta"/>, clamped to [0, MaxCount].</summary>
    public static int ChangeCount( int delta ) => DesiredCount = Math.Clamp( DesiredCount + delta, 0, MaxCount );

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
