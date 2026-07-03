/// <summary>
/// Per-player lobby ready state. Holds the networked <see cref="IsReady"/>
/// bool that <see cref="LobbyReadyUp"/> flips when the player enters/exits
/// the ready zone, and drives the player's overhead indicator color. 
/// Also exposed to the lobby board UI, which reads <see cref="DisplayName"/> 
/// and <see cref="IsReady"/> to render each connected player's ready state.
/// </summary>
public sealed class PlayerReadyState : Component
{
    [Property] public ModelRenderer Circle { get; set; }

    /// <summary>
    /// Overhead crown renderer, shown only on the player who won the most recent round.
    /// Lives on the player prefab so it persists across the lobby↔game scene swaps.
    /// </summary>
    [Property] public ModelRenderer Crown { get; set; }

    [Sync, Change( nameof( OnIsReadyChanged ) )]
    public bool IsReady { get; set; }

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
        // Only show the overhead indicator when there's a lobby in this scene.
        ShowIndicator( LobbyManager.Current != null );
        ApplyTint( IsReady );
        ApplyCrownVisibility();
    }

    /// <summary>Show or hide the overhead indicator. The lobby toggles this on while we're in the lobby scene.</summary>
    public void ShowIndicator( bool visible )
    {
        if ( Circle.IsValid() ) Circle.Enabled = visible;
    }

    private void OnIsReadyChanged( bool oldValue, bool newValue )
    {
        ApplyTint( newValue );
    }

    private void ApplyTint( bool ready )
    {
        if ( Circle is null ) return;
        Circle.Tint = ready ? Color.Green : Color.Red;
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
