using System.Collections.Generic;

/// <summary>
/// Session-wide, per-player win count store. Every client keeps its own
/// copy, kept in step by the broadcast RPCs in <see cref="VictoryManager"/> (record
/// a win) and <see cref="LobbyNetworkSpawner"/> (snapshot for late joiners). Because
/// every client applies every increment locally, in the event of host migration it is a no-op,
/// the new host already has the full history.
/// </summary>
public static class Leaderboard
{
    private static readonly Dictionary<ulong, int> _wins = new();

    /// <summary>Steam ID of the player who won the most recent round, or 0 if no round has been played yet.</summary>
    public static ulong PreviousWinnerSteamId { get; private set; }

    /// <summary>Fires whenever the store changes (a win recorded, or a snapshot applied).</summary>
    public static event System.Action Changed;

    public static int GetWins( ulong steamId )
    {
        return _wins.GetValueOrDefault( steamId, 0 );
    }

    public static void RecordWin( ulong steamId )
    {
        if ( steamId == 0 ) return;
        _wins[steamId] = GetWins( steamId ) + 1;
        PreviousWinnerSteamId = steamId;
        Changed?.Invoke();
    }

    // Overwrite the local store with the host's authoritative view. Safe to call on
    // clients that are already in sync, they just re-set the same values.
    public static void ApplySnapshot( ulong[] steamIds, int[] counts, ulong previousWinnerSteamId )
    {
        if ( steamIds == null || counts == null ) return;
        _wins.Clear();
        int n = System.Math.Min( steamIds.Length, counts.Length );
        for ( int i = 0; i < n; i++ )
        {
            if ( steamIds[i] == 0 ) continue;
            _wins[steamIds[i]] = counts[i];
        }
        PreviousWinnerSteamId = previousWinnerSteamId;
        Changed?.Invoke();
    }

    // Return a snapshot of the current store for broadcasting to late joiners.
    public static (ulong[] ids, int[] counts, ulong previousWinnerSteamId) Snapshot()
    {
        int count = _wins.Count;
        ulong[] ids = new ulong[count];
        int[] counts = new int[count];
        int i = 0;
        foreach ( KeyValuePair<ulong, int> kvp in _wins )
        {
            ids[i] = kvp.Key;
            counts[i] = kvp.Value;
            i++;
        }
        return (ids, counts, PreviousWinnerSteamId);
    }
}
