/// <summary>
/// Thin wrapper over <see cref="Sandbox.Services.Stats"/> for the local player.
/// Every method operates on the client it's called from, so it must be invoked
/// via a broadcast/filtered RPC by whoever owns the game logic (host).
///
/// Stat identifiers used here need matching entries + achievements in the
/// sbox.game dashboard. Suggested aggregation: <c>longest_survival_seconds</c> as
/// max, the rest as sum.
/// </summary>
public static class PlayerStats
{
    private static RealTimeSince _sinceMatchStart;
    private static bool _isMatchInProgress;

    public static void StartMatchTimer()
    {
        _sinceMatchStart = 0f;
        _isMatchInProgress = true;
    }

    public static void RecordWin()
    {
        Sandbox.Services.Stats.Increment( "matches_won", 1 );
        FinishMatch();
    }

    public static void RecordElimination()
    {
        FinishMatch();
    }

    private static void FinishMatch()
    {
        if ( !_isMatchInProgress ) return;
        _isMatchInProgress = false;
        Sandbox.Services.Stats.Increment( "matches_played", 1 );
        Sandbox.Services.Stats.SetValue( "longest_survival_seconds", (float)_sinceMatchStart );
    }
}
