public sealed class PlayerManager : Component, Component.INetworkListener
{
	[Property] public GameObject PlayerPrefab { get; set; }
	[Property] public TileManager TileManager { get; set; }

	/// <summary>
	/// Spawns player characters at random tile positions. Called by GameManager when the game starts.
	/// If <see cref="Bots.IsEnabled"/> is on, also spawns <see cref="Bots.BotCount"/>
	/// bots at additional random tile positions.
	/// </summary>
	public void SpawnPlayers()
	{
		if ( !Networking.IsHost ) return;

		var AvailableSpawnPositions = TileManager.AvailableSpawnLocations;
		var randomSeed = System.DateTime.Now.Millisecond;
		Sandbox.Game.SetRandomSeed( randomSeed );

		foreach ( var client in Connection.All )
		{
			Vector3 position = TakeSpawnPosition( AvailableSpawnPositions );
			var newTransform = new Transform( position, Rotation.Identity, Vector3.One );
			var player = PlayerPrefab.Clone( newTransform, name: $"Player - {client.Name}" );
			player.GetComponent<PlayerController>().UseInputControls = false;
			player.NetworkSpawn( client );
		}

		if ( Bots.IsEnabled )
		{
			for ( int i = 0; i < Bots.BotCount; i++ )
			{
				Vector3 position = TakeSpawnPosition( AvailableSpawnPositions );
				BotManager.SpawnAt( PlayerPrefab, new Transform( position, Rotation.Identity, Vector3.One ), i );
			}
		}
	}

	private static Vector3 TakeSpawnPosition( List<Vector3> positions )
	{
		if ( positions == null || positions.Count == 0 ) return Vector3.Zero;
		Vector3 selected = Sandbox.Game.Random.FromList( positions );
		positions.Remove( selected );
		return selected;
	}

	public void EnablePlayersInput()
	{
		EnablePlayersInputNetwork();
	}

	/// <summary>
	/// Host-only. Removes a player from the game by destroying their networked
	/// GameObject. The destroy propagates to every client.
	/// </summary>
	public void DestroyPlayer( PlayerController player )
	{
		if ( !Networking.IsHost ) return;
		if ( player == null || !player.IsValid() ) return;
		player.GameObject.Destroy();
	}

	[Rpc.Broadcast]
	private void EnablePlayersInputNetwork()
	{
		foreach ( var pc in Scene.GetAllComponents<PlayerController>() )
		{
			// Bots must stay input-suppressed — otherwise the host's live input
			// drives every bot's PlayerController.
			if ( pc.IsBot() ) continue;
			pc.UseInputControls = true;
		}
	}
}
