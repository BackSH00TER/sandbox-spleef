using System.Linq;

/// <summary>
/// Trigger zone in the lobby that toggles <see cref="BotConfig.IsEnabled"/> on
/// enter/exit. Behaves like <see cref="LobbyReadyUp"/> — only real players
/// (non-bots) can flip the toggle so a bot walking into it doesn't feedback-loop.
/// Host owns the state and fans the change to every client via broadcast RPC.
/// </summary>
public sealed class LobbyAIToggle : Component, Component.ITriggerListener
{
	public void OnTriggerEnter( Collider other )
	{
		if ( !ShouldRespondTo( other ) ) return;

		// Any client's trigger fires this — the broadcast RPC fans to everyone,
		// and only the host actually spawns/despawns bots inside BroadcastSetAI.
		BroadcastSetAI( !BotConfig.IsEnabled );
	}

	public void OnTriggerExit( Collider other )
	{
		// Intentionally no-op — this is a toggle, not a hold. Stepping out doesn't
		// flip it back so the state persists between rounds.
	}

	private static bool ShouldRespondTo( Collider other )
	{
		GameObject root = other.GameObject.Root;
		if ( !root.IsValid() ) return false;
		if ( root.GetComponent<PlayerController>() == null ) return false;
		// Ignore bots stepping in — they shouldn't self-toggle.
		if ( root.IsBot() ) return false;
		return true;
	}

	[Rpc.Broadcast]
	private void BroadcastSetAI( bool enabled )
	{
		BotConfig.SetEnabled( enabled );

		// Only the host reconciles the actual spawn list; other clients just update
		// their local flag so any UI reading BotConfig.IsEnabled stays in sync.
		if ( !Networking.IsHost ) return;

		BotDirector director = Scene.GetAllComponents<BotDirector>().FirstOrDefault();
		director?.Reconcile();
	}
}
