/// <summary>
/// Per-player lobby ready state. Holds the networked <see cref="IsReady"/>
/// bool that <see cref="LobbyReadyUp"/> flips when the player enters/exits
/// the ready zone.
/// </summary>
public sealed class PlayerReadyState : Component
{
    /// <summary>
    /// Overhead crown renderer, shown only on the player who won the most recent round.
    /// Lives on the player prefab so it persists across the lobby↔game scene swaps.
    /// </summary>
    [Property] public ModelRenderer Crown { get; set; }

    [Sync] public bool IsReady { get; set; }

    public string DisplayName => GameObject.GetPlayerName();

    // Bots don't have a real SteamId (they're host-owned, so Network.Owner.SteamId is the
    // host's). Use a per-slot bot id as their synthetic identity for leaderboard/crown
    // lookups so wins/crowns don't smear onto the host and persist across respawns.
    public string LeaderboardId
    {
        get
        {
            BotController bot = GetComponent<BotController>();
            if ( bot != null ) return Leaderboard.BotId( bot.Slot );
            ulong steamId = Network.Owner?.SteamId ?? 0UL;
            return steamId == 0UL ? null : Leaderboard.PlayerId( steamId );
        }
    }

    public int WinCount => Leaderboard.GetWins( LeaderboardId );

    protected override void OnStart()
    {
        RefreshCrown();
    }

    public void RefreshCrown()
    {
        if ( !Crown.IsValid() ) return;
        Crown.GameObject.Enabled = false;

        string id = LeaderboardId;
        if ( !string.IsNullOrEmpty( id ) && id == Leaderboard.PreviousWinnerId )
        {
            Crown.GameObject.Enabled = true;
        }
    }
}
