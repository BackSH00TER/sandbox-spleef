using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Trigger zone in the lobby that toggles <see cref="Bots.IsEnabled"/> whenever
/// a real player walks in. Behaves like <see cref="LobbyReadyUp"/> — bots are
/// filtered so they can't self-toggle. Host owns the state and fans the change
/// to every client via broadcast RPC.
/// </summary>
public sealed class LobbyAIToggle : Component, Component.ITriggerListener
{
	// A single player often has multiple colliders (body, feet, etc.), each of
	// which fires OnTriggerEnter/Exit separately. Tracking roots means we only
	// react on the empty→occupied transition and ignore duplicate enters, which
	// would otherwise flip the toggle twice per walk-through.
	private readonly HashSet<GameObject> _presentRoots = new();

	public void OnTriggerEnter( Collider other )
	{
		GameObject root = GetTriggeringRoot( other );
		if ( root == null ) return;

		if ( !_presentRoots.Add( root ) ) return;
		if ( _presentRoots.Count != 1 ) return;

		BroadcastSetAI( !Bots.IsEnabled );
	}

	public void OnTriggerExit( Collider other )
	{
		GameObject root = GetTriggeringRoot( other );
		if ( root == null ) return;

		_presentRoots.Remove( root );
	}

	private static GameObject GetTriggeringRoot( Collider other )
	{
		GameObject root = other.GameObject.Root;
		if ( !root.IsValid() ) return null;
		if ( root.GetComponent<PlayerController>() == null ) return null;
		// Ignore bots stepping in — they shouldn't self-toggle.
		if ( root.IsBot() ) return null;
		return root;
	}

	[Rpc.Broadcast]
	private void BroadcastSetAI( bool enabled )
	{
		Bots.SetEnabled( enabled );

		// Only the host reconciles the actual spawn list; other clients just update
		// their local flag so any UI reading Bots.IsEnabled stays in sync.
		if ( !Networking.IsHost ) return;

		BotManager botManager = Scene.GetAllComponents<BotManager>().FirstOrDefault();
		botManager?.SyncBotCount();
	}
}
