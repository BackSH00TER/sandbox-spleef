using Sandbox;

/// <summary>
/// Player-name helpers. Handles the bot-vs-real-player fork in one place so callers
/// (UI, HUDs, log lines, spectator, victory) don't have to check <see cref="BotBrain"/>
/// themselves.
/// </summary>
public static class PlayerNameExtensions
{
    /// <summary>
    /// Returns the display name for a player-owned GameObject: the bot's synthetic name
    /// if a <see cref="BotBrain"/> is present, otherwise the owning connection's Steam
    /// display name. Falls back to "Unknown" for null / owner-less objects.
    /// </summary>
    public static string GetPlayerName( this GameObject go )
    {
        if ( !go.IsValid() ) return "Unknown";
        BotBrain bot = go.GetComponent<BotBrain>();
        if ( bot != null ) return bot.BotName;
        return go.Network?.Owner?.DisplayName ?? "Unknown";
    }

    /// <summary>Convenience overload for callers holding a Component (e.g. PlayerController).</summary>
    public static string GetPlayerName( this Component component )
    {
        if ( !component.IsValid() ) return "Unknown";
        return component.GameObject.GetPlayerName();
    }
}
