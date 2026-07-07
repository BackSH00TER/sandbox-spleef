using System.Collections.Generic;

/// <summary>
/// Session-wide, per-player win count store. Every client keeps its own
/// copy, kept in step by the broadcast RPCs in <see cref="VictoryManager"/> (record
/// a win) and <see cref="LobbyNetworkSpawner"/> (snapshot for late joiners). Because
/// every client applies every increment locally, in the event of host migration it is a no-op,
/// the new host already has the full history.
///
/// Ids are strings of the form <c>"steam:{steamId}"</c> for real players or
/// <c>"bot:{slot}"</c> for AI bots. Use <see cref="PlayerId"/> / <see cref="BotId"/>
/// to construct them rather than hand-formatting.
/// </summary>
public static class Leaderboard
{
    private static readonly Dictionary<string, int> _wins = new();

    /// <summary>Id of the player who won the most recent round, or null if no round has been played yet.</summary>
    public static string PreviousWinnerId { get; private set; }

    public static string PlayerId( ulong steamId ) => $"steam:{steamId}";
    public static string BotId( int slot ) => $"bot:{slot}";

    public static int GetWins( string id )
    {
        if ( string.IsNullOrEmpty( id ) ) return 0;
        return _wins.GetValueOrDefault( id, 0 );
    }

    public static void RecordWin( string id )
    {
        if ( string.IsNullOrEmpty( id ) ) return;
        _wins[id] = GetWins( id ) + 1;
        PreviousWinnerId = id;
    }

    // Overwrite the local store with the host's authoritative view. Safe to call on
    // clients that are already in sync, they just re-set the same values.
    public static void ApplySnapshot( string[] ids, int[] counts, string previousWinnerId )
    {
        if ( ids == null || counts == null ) return;
        _wins.Clear();
        int n = System.Math.Min( ids.Length, counts.Length );
        for ( int i = 0; i < n; i++ )
        {
            if ( string.IsNullOrEmpty( ids[i] ) ) continue;
            _wins[ids[i]] = counts[i];
        }
        PreviousWinnerId = previousWinnerId;
    }

    // Return a snapshot of the current store for broadcasting to late joiners.
    public static (string[] ids, int[] counts, string previousWinnerId) Snapshot()
    {
        int count = _wins.Count;
        string[] ids = new string[count];
        int[] counts = new int[count];
        int i = 0;
        foreach ( KeyValuePair<string, int> kvp in _wins )
        {
            ids[i] = kvp.Key;
            counts[i] = kvp.Value;
            i++;
        }
        return (ids, counts, PreviousWinnerId);
    }
}
