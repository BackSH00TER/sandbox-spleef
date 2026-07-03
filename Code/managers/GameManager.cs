using System;
using System.Linq;
using Sandbox;

/// <summary>
/// Owns the core game loop: building the arena, spawning players, running the
/// pre-game countdown, and detecting eliminations. Once only one player remains it
/// hands off to <see cref="VictoryManager"/> for the post-game victory sequence.
/// </summary>
public sealed class GameManager : Component, Component.INetworkListener
{
	[Property] TileManager TileManager { get; set; }
	[Property] PlayerManager PlayerManager { get; set; }
	[Property] VictoryManager VictoryManager { get; set; }
	[Property] public SoundEvent EliminatedSound { get; set; }

	[Property] public GameObject CountdownDronePrefab { get; set; }
	[Property] public Vector3 CountdownDroneSpawnOffset { get; set; } = new Vector3( 0f, 0f, 220f );

	[Property, Group( "Debug" )] public bool Debug_DisableGridPhysics { get; set; } = false;

	public TimeUntil CountdownTimer = 5f;
	public bool CountdownActive { get; private set; } = false;
	public bool GameInProgress { get; private set; } = false;

	public static GameManager Current { get; private set; }

	protected override void OnEnabled()
	{
		Current = this;
	}

	protected override void OnDisabled()
	{
		if ( Current == this )
			Current = null;
	}

	protected override void OnStart()
	{
		if ( !Networking.IsHost ) return;

		TileManager.BuildGrid();
		SpawnCountdownDrone();
		PlayerManager.SpawnPlayers();
		CountdownActive = true;
	}

	private void SpawnCountdownDrone()
	{
		if ( CountdownDronePrefab == null ) return;

		Vector3 spawnPos = TileManager.IsValid()
			? TileManager.WorldPosition + CountdownDroneSpawnOffset
			: WorldPosition + CountdownDroneSpawnOffset;

		GameObject drone = CountdownDronePrefab.Clone( new CloneConfig
		{
			Transform = new Transform( spawnPos ),
			Name = "CountdownDrone"
		} );
		drone.NetworkSpawn();
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;

		if ( CountdownActive && CountdownTimer <= 0f )
		{
			GameInProgress = true;
			CountdownActive = false;
			if ( !Debug_DisableGridPhysics ) TileManager.ActivateGrid();
			PlayerManager.EnablePlayersInput();
			BroadcastMatchStart();
		}
	}

	public void StartGame()
	{
		if ( !Networking.IsHost ) return;

		GameInProgress = true;
		CountdownActive = false;
		if ( !Debug_DisableGridPhysics ) TileManager.ActivateGrid();
		PlayerManager.EnablePlayersInput();
	}

	public void PlayerEliminated( PlayerController player )
	{
		if ( !Networking.IsHost ) return;
		if ( player == null || !player.IsValid() ) return;

		string name = player.Network?.Owner?.DisplayName ?? player.GameObject.Name;

		// Destroy hasn't propagated yet, so filter out the eliminated player explicitly.
		List<PlayerController> remaining = Scene.GetAllComponents<PlayerController>()
			.Where( p => p.IsValid() && p != player )
			.ToList();

		Log.Info( $"Player '{name}' eliminated. Players remaining: {remaining.Count}" );

		BroadcastPlayEliminationSound();

		// Capture the owning connection before we destroy the player — Network.Owner
		// becomes unreachable once the GameObject is gone.
		Connection ownerConnection = player.Network?.Owner;
		if ( ownerConnection != null )
		{
			using ( Rpc.FilterInclude( ownerConnection ) )
			{
				EnterSpectatorAfterElimination();
				NotifyLocalElimination();
			}
		}

		PlayerManager.DestroyPlayer( player );

		if ( remaining.Count <= 1 )
		{
			GameInProgress = false;
			VictoryManager?.BeginVictory( remaining.FirstOrDefault() );
		}
	}

	// Fanned out so every client plays the elimination sound locally
	[Rpc.Broadcast]
	private void BroadcastPlayEliminationSound()
	{
		if ( EliminatedSound == null ) return;
		SoundHandle handle = Sound.Play( EliminatedSound );
		if ( !handle.IsValid() ) return;
		handle.Volume = 0.5f;
		// SoundEvent is 3D — without ListenLocal it plays at world origin, which could be
		// inaudible to a player who is not near it, (ie: a player who fell into the killbox).
		handle.ListenLocal = true;
	}

	// Caller is expected to wrap this in Rpc.FilterInclude( ownerConnection ) so it
	// only runs on the eliminated player's client. Without that filter every client
	// would enter spectator mode.
	[Rpc.Broadcast]
	private void EnterSpectatorAfterElimination()
	{
		SpectatorMode.Current?.Activate();
	}

	// Only the eliminated player's client should record their own survival stat.
	[Rpc.Broadcast]
	private void NotifyLocalElimination()
	{
		PlayerStats.RecordElimination();
	}

	// Fans out to every client so each one starts its local match timer.
	[Rpc.Broadcast]
	private void BroadcastMatchStart()
	{
		PlayerStats.StartMatchTimer();
	}

	/// <summary>
	/// Host-only. Called when a client connects to the server. Players who join
	/// after the game has started don't get a player controller — instead we tell
	/// their client to drop straight into spectator mode.
	/// </summary>
	public void OnActive( Connection newConnection )
	{
		if ( !Networking.IsHost ) return;

		if ( GameInProgress )
		{
			Log.Info( $"Game already in progress; sending '{newConnection.Name}' into spectator mode." );

			// Scoped filter so the [Rpc.Broadcast] call below is delivered only to the
			// joining connection instead of fanning out to every client.
			using ( Rpc.FilterInclude( newConnection ) )
			{
				JoinAsSpectator();
			}
		}
	}

	// Caller is expected to wrap this in Rpc.FilterInclude( newConnection ) so it only runs
	// on the joining client. Without that filter everyone would flip into spectator mode.
	[Rpc.Broadcast]
	private void JoinAsSpectator()
	{
		Log.Info( "Joining as spectator." );
		SpectatorMode.Current?.Activate();
	}

	// Fires on the peer that just inherited the host role after a migration. The game's
	// host-only state doesn't survive migration cleanly, so bail everyone back to the lobby.
	public void OnBecameHost( Connection previousHost )
	{
		string lobbyScenePath = VictoryManager?.SceneToLoadFinish?.ResourcePath;
		if ( string.IsNullOrEmpty( lobbyScenePath ) )
		{
			Log.Error( "GameManager: no lobby scene assigned on VictoryManager, can't return to lobby after host migration." );
			return;
		}

		Log.Info( "Host migrated. Returning everyone to lobby." );
		BroadcastReturnToLobby( "Host has left the game.", lobbyScenePath );
	}

	[Rpc.Broadcast]
	private void BroadcastReturnToLobby( string reason, string lobbyScenePath )
	{
		LobbyManager.ShowLobbyMessage( reason );
		Game.ActiveScene.LoadFromFile( lobbyScenePath );
	}
}
