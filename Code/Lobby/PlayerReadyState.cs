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

    public string DisplayName => Network.Owner?.DisplayName ?? "Unknown";

    public int WinCount
    {
        get
        {
            ulong steamId = Network.Owner?.SteamId ?? 0UL;
            if ( steamId == 0UL ) return 0;
            return Leaderboard.GetWins( steamId );
        }
    }

    protected override void OnEnabled()
    {
        Leaderboard.Changed += ApplyCrownVisibility;
    }

    protected override void OnDisabled()
    {
        Leaderboard.Changed -= ApplyCrownVisibility;
    }

    protected override void OnStart()
    {
        ApplyCrownVisibility();
    }

    private void ApplyCrownVisibility()
    {
        if ( !Crown.IsValid() ) return;
        ulong steamId = Network.Owner?.SteamId ?? 0UL;
        bool shouldShow = steamId != 0UL && steamId == Leaderboard.PreviousWinnerSteamId;
        if ( Crown.Enabled != shouldShow )
        {
            Crown.Enabled = shouldShow;
        }
    }
}
