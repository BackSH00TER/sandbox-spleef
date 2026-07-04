/// <summary>
/// Session-wide toggle for whether AI bots participate. Flipped by
/// <see cref="LobbyAIToggle"/> in the lobby and read by <see cref="BotDirector"/>
/// (which owns the spawn/despawn). Every client keeps a copy kept in step by
/// the broadcast RPC in <see cref="LobbyAIToggle"/>, so host migration is a no-op.
/// </summary>
public static class BotConfig
{
	/// <summary>How many bots to spawn when <see cref="IsEnabled"/> is true.</summary>
	public const int BotCount = 3;

	public static bool IsEnabled { get; private set; }

	public static void SetEnabled( bool enabled )
	{
		IsEnabled = enabled;
	}
}
