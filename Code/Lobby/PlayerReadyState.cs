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
    // host's). Use BotBrain.LeaderboardId as their synthetic identity for leaderboard/crown
    // lookups so wins/crowns don't smear onto the host.
    public ulong LeaderboardId
    {
        get
        {
            BotBrain bot = GetComponent<BotBrain>();
            if ( bot != null ) return bot.LeaderboardId;
            return Network.Owner?.SteamId ?? 0UL;
        }
    }

    public int WinCount
    {
        get
        {
            ulong id = LeaderboardId;
            if ( id == 0UL ) return 0;
            return Leaderboard.GetWins( id );
        }
    }

    protected override void OnStart()
    {
        RefreshCrown();
    }

    public void RefreshCrown()
    {
        if ( !Crown.IsValid() ) return;
        Crown.GameObject.Enabled = false;

        ulong id = LeaderboardId;
        if ( id != 0UL && id == Leaderboard.PreviousWinnerSteamId )
        {
            Crown.GameObject.Enabled = true;
        }
    }
}
