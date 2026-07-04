using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Host-only. Owns bot spawn/despawn in whatever scene it lives in (lobby uses
/// this to populate the lobby view; the game scene has its own bot spawn path
/// via <see cref="PlayerManager"/>). Keeps the live bot count in sync with
/// <see cref="Bots.DesiredCount"/> whenever asked, and once at start.
/// </summary>
public sealed class BotManager : Component
{
	/// <summary>Prefab to clone for each bot. Same prefab as real players.</summary>
	[Property] public GameObject BotPrefab { get; set; }

	protected override void OnStart()
	{
		if ( !Networking.IsHost ) return;
		SyncBotCount();
	}

	/// <summary>Bring the live bot count in line with <see cref="Bots.DesiredCount"/>.</summary>
	public void SyncBotCount()
	{
		if ( !Networking.IsHost ) return;

		List<BotController> existing = Scene.GetAllComponents<BotController>().ToList();
		int desired = Bots.DesiredCount;

		while ( existing.Count > desired )
		{
			BotController victim = existing[existing.Count - 1];
			existing.RemoveAt( existing.Count - 1 );
			victim.GameObject.Destroy();
		}

		while ( existing.Count < desired )
		{
			BotController spawned = SpawnBot( existing.Count );
			if ( spawned != null )
			{
				existing.Add( spawned );
			}
			else
			{
				break;
			}
		}
	}

	private BotController SpawnBot( int slot )
	{
		// Use the lobby's normal spawn distribution so bots mix in with real players.
		LobbyNetworkSpawner spawner = Scene.GetAllComponents<LobbyNetworkSpawner>().FirstOrDefault();
		Transform spawnTransform = spawner != null ? spawner.FindSpawnLocation() : WorldTransform;
		return SpawnAt( BotPrefab, spawnTransform, slot );
	}

	/// <summary>
	/// Host-only. Spawn a bot at the given transform. <paramref name="slot"/> keys per-slot identity (name, outfit).
	/// </summary>
	public static BotController SpawnAt( GameObject prefab, Transform transform, int slot )
	{
		if ( !Networking.IsHost ) return null;
		if ( !prefab.IsValid() )
		{
			Log.Warning( "BotManager.SpawnAt: prefab is not assigned." );
			return null;
		}

		string name = Bots.NameForSlot( slot );
		GameObject bot = prefab.Clone( transform.WithScale( 1f ), name: $"Bot - {name}" );

		PlayerController pc = bot.GetComponent<PlayerController>();
		if ( pc.IsValid() )
		{
			pc.UseInputControls = false;
		}

		BotController controller = bot.GetComponent<BotController>() ?? bot.AddComponent<BotController>();

		// NetworkSpawn with no connection = host-owned. BotController will run its
		// (future) decision logic on the owner.
		bot.NetworkSpawn();

		controller.Initialize( name, slot );
		return controller;
	}
}
