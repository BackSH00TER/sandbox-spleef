using Sandbox;

/// <summary>
/// Bot detection helpers. Presence of a <see cref="BotBrain"/> component identifies
/// a bot; wrapped in extension methods so callers read as <c>player.IsBot()</c>
/// instead of the verbose <c>GetComponent&lt;BotBrain&gt;() != null</c>.
/// </summary>
public static class BotExtensions
{
    public static bool IsBot( this GameObject go )
    {
        if ( !go.IsValid() ) return false;
        return go.Components.Get<BotBrain>() != null;
    }

    public static bool IsBot( this Component component )
    {
        if ( !component.IsValid() ) return false;
        return component.GameObject.IsBot();
    }
}
