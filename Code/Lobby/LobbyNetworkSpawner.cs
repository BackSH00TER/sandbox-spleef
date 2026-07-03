using System.Linq;
using System.Threading.Tasks;
using Sandbox;

/// <summary>
/// Drop-in replacement for <see cref="Sandbox.NetworkHelper"/> in the lobby scene.
/// Handles three spawn cases:
/// <list type="bullet">
/// <item>Initial host startup — creates the lobby and (via <see cref="OnActive"/>) spawns the host.</item>
/// <item>A client joining a live lobby — spawns their player via <see cref="OnActive"/>.</item>
/// <item>Game→lobby scene reload — host-only <see cref="OnStart"/> pass iterates <see cref="Connection.All"/>
/// and spawns a player for any connection that doesn't already have one. Needed because the
/// per-client <c>Scene.Load</c> swap used by <c>VictoryManager.BroadcastLoadLobbyScene</c>
/// doesn't re-fire <see cref="Component.INetworkListener.OnActive"/> for existing connections.</item>
/// </list>
/// </summary>
public sealed class LobbyNetworkSpawner : Component, Component.INetworkListener
{
    /// <summary>Create a lobby on first load if one isn't already active.</summary>
    [Property] public bool StartServer { get; set; } = true;

    /// <summary>Prefab cloned for each connected player.</summary>
    [Property] public GameObject PlayerPrefab { get; set; }

    /// <summary>Size of the box (centered on each <see cref="SpawnPoint"/>) that players are randomly spawned in.</summary>
    [Property] public Vector3 SpawnAreaSize { get; set; } = new Vector3( 150f, 150f, 0f );

    /// <summary>Draw the spawn jitter box around each <see cref="SpawnPoint"/> in the editor.</summary>
    [Property] public bool ShouldDrawSpawnGizmo { get; set; } = false;

    // Async, runs before OnStart. On the host's very first lobby load Networking isn't
    // active yet — we create the lobby here. Game→lobby reloads skip this branch because
    // Networking is already active by then.
    protected override async Task OnLoad()
    {
        if ( Scene.IsEditor ) return;

        if ( StartServer && !Networking.IsActive )
        {
            LoadingScreen.Title = "Creating Lobby";
            await Task.DelayRealtimeSeconds( 0.1f );
            Networking.CreateLobby( new() );
        }
    }

    // Handles transition from game scene to lobby scene to spawn players.
    // The per-client scene swap doesn't re-fire OnActive for connections that were already joined, 
    // so the host spawns any missing players here.
    protected override void OnStart()
    {
        if ( !Networking.IsHost ) return;
        if ( !Networking.IsActive ) return;

        foreach ( Connection client in Connection.All )
        {
            if ( HasPlayerControllerFor( client ) ) continue;
            Log.Info( $"LobbyNetworkSpawner: OnStart for {client}, spawning player." );
            SpawnFor( client );
        }
    }

    // Handles the initial bootup and any client joining a live lobby to spawn the player and synchronize the leaderboard.
    // Existing connections on a scene reload are covered by OnStart instead.
    public void OnActive( Connection channel )
    {
        // Guard against double-spawn — OnStart may have already covered this connection.
        if ( !HasPlayerControllerFor( channel ) )
        {
            Log.Info( $"LobbyNetworkSpawner: OnActive for {channel}, spawning player." );
            SpawnFor( channel );
        }

        // Send the current leaderboard just to the new joiner so they aren't stuck at 0/0
        // for players who racked up wins before they joined.
        if ( Networking.IsHost )
        {
            (ulong[] ids, int[] counts, ulong previousWinnerSteamId) = Leaderboard.Snapshot();
            using ( Rpc.FilterInclude( channel ) )
            {
                BroadcastLeaderboardSnapshot( ids, counts, previousWinnerSteamId );
            }
        }
    }

    [Rpc.Broadcast]
    private void BroadcastLeaderboardSnapshot( ulong[] steamIds, int[] counts, ulong previousWinnerSteamId )
    {
        Leaderboard.ApplySnapshot( steamIds, counts, previousWinnerSteamId );
    }

    // Returns true if the given connection already has a player controller in the scene.
    private bool HasPlayerControllerFor( Connection client )
    {
        return Scene.GetAllComponents<PlayerController>()
            .Any( pc => pc.IsValid() && pc.Network.Active && pc.Network.OwnerId == client.Id );
    }

    // Spawn a player for the given connection. The host owns this logic, and the engine
    // will automatically replicate the new player to the client.
    private void SpawnFor( Connection client )
    {
        if ( !PlayerPrefab.IsValid() ) return;

        Transform spawnTransform = FindSpawnLocation().WithScale( 1f );
        GameObject player = PlayerPrefab.Clone( spawnTransform, name: $"Player - {client.Name}" );
        player.NetworkSpawn( client );
    }

    // Pick a random spawn point and jitter it inside SpawnAreaSize.
    private Transform FindSpawnLocation()
    {
        SpawnPoint[] points = Scene.GetAllComponents<SpawnPoint>().ToArray();
        if ( points.Length == 0 ) return WorldTransform;

        SpawnPoint anchor = Game.Random.FromArray( points );
        Transform anchorTransform = anchor.WorldTransform;

        Vector3 half = SpawnAreaSize * 0.5f;
        Vector3 offset = new Vector3(
            Game.Random.Float( -half.x, half.x ),
            Game.Random.Float( -half.y, half.y ),
            Game.Random.Float( -half.z, half.z )
        );
        return anchorTransform.WithPosition( anchorTransform.Position + offset );
    }

    // Debug element that draws a gizmo around the spawn area to visualize the area.
    protected override void DrawGizmos()
    {
        if ( !ShouldDrawSpawnGizmo ) return;
        if ( SpawnAreaSize.IsNearZeroLength ) return;

        SpawnPoint[] points = Scene.GetAllComponents<SpawnPoint>().ToArray();
        if ( points.Length == 0 ) return;

        Gizmo.Draw.Color = Gizmo.IsSelected ? Color.Cyan : Color.Cyan.WithAlpha( 0.4f );
        BBox box = BBox.FromPositionAndSize( Vector3.Zero, SpawnAreaSize );

        foreach ( SpawnPoint point in points )
        {
            using ( Gizmo.Scope( "lobby-spawn-area", point.WorldTransform ) )
            {
                Gizmo.Draw.LineBBox( box );
            }
        }
    }
}
