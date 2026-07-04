using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Host-only. Owns bot spawn/despawn in whatever scene it lives in (lobby uses
/// this to populate the lobby view; the game scene has its own bot spawn path
/// via <see cref="PlayerManager"/>). Reconciles the live bot list against
/// <see cref="BotConfig.IsEnabled"/> whenever asked, and once at start.
/// </summary>
public sealed class BotDirector : Component
{
	/// <summary>Prefab to clone for each bot. Same prefab as real players.</summary>
	[Property] public GameObject BotPrefab { get; set; }

	protected override void OnStart()
	{
		if ( !Networking.IsHost ) return;
		Reconcile();
	}

	/// <summary>Bring the live bot count in line with <see cref="BotConfig.IsEnabled"/>.</summary>
	public void Reconcile()
	{
		if ( !Networking.IsHost ) return;

		List<BotBrain> existing = Scene.GetAllComponents<BotBrain>().ToList();
		int desired = BotConfig.IsEnabled ? BotConfig.BotCount : 0;

		while ( existing.Count > desired )
		{
			BotBrain victim = existing[existing.Count - 1];
			existing.RemoveAt( existing.Count - 1 );
			victim.GameObject.Destroy();
		}

		while ( existing.Count < desired )
		{
			BotBrain spawned = SpawnBot();
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

	private BotBrain SpawnBot()
	{
		// Use the lobby's normal spawn distribution so bots mix in with real players.
		LobbyNetworkSpawner spawner = Scene.GetAllComponents<LobbyNetworkSpawner>().FirstOrDefault();
		Transform spawnTransform = spawner != null ? spawner.FindSpawnLocation() : WorldTransform;
		return SpawnAt( BotPrefab, spawnTransform );
	}

	/// <summary>
	/// Host-only. Clone the given prefab at the given transform, tag it as a bot,
	/// and network-spawn it host-owned. Used by both this director (lobby scene)
	/// and <see cref="PlayerManager"/> (game scene, spawns at tile positions).
	/// </summary>
	public static BotBrain SpawnAt( GameObject prefab, Transform transform )
	{
		if ( !Networking.IsHost ) return null;
		if ( !prefab.IsValid() )
		{
			Log.Warning( "BotDirector.SpawnAt: prefab is not assigned." );
			return null;
		}

		string name = BotNames.Random();
		GameObject bot = prefab.Clone( transform.WithScale( 1f ), name: $"Bot - {name}" );

		PlayerController pc = bot.GetComponent<PlayerController>();
		if ( pc.IsValid() )
		{
			pc.UseInputControls = false;
		}

		BotBrain brain = bot.GetComponent<BotBrain>() ?? bot.AddComponent<BotBrain>();

		// NetworkSpawn with no connection = host-owned. BotBrain will run its
		// (future) decision logic on the owner.
		bot.NetworkSpawn();

		brain.Initialize( name );
		return brain;
	}
}
