using System;
using System.Collections.Generic;
using Sandbox;

/// <summary>
/// Handles the post-game victory sequence: declaring the winner, freezing them on a
/// gold podium, raining confetti, disintegrating the arena, and finally swapping back
/// to the lobby scene. <see cref="GameManager"/> kicks this off via <see cref="BeginVictory"/>
/// once only one player remains.
/// </summary>
public sealed class VictoryManager : Component
{
    [Property] TileManager TileManager { get; set; }
    [Property] public GameObject ConfettiPrefab { get; set; }
    [Property] public SoundEvent ConfettiSound { get; set; }
    [Property] public SoundEvent VictoryMusic { get; set; }
    [Property] public SceneFile SceneToLoadFinish { get; set; }

    /// <summary>Total length of the victory phase, from winner declared to scene swap.</summary>
    public const float ResultsDuration = 7f;
    /// <summary>How long the outward tile-drop wave takes inside <see cref="ResultsDuration"/>.</summary>
    public const float DisintegrationDuration = 5f;
    // Seconds the screen takes to fade to black at the tail of the results window.
    private const float ResultsFadeDuration = 2f;
    // Seconds the victory music ramps down for at the tail of the results window so the scene swap doesn't cut it off abruptly.
    private const float VictoryMusicFadeDuration = 3f;

    [Sync] public bool IsShowingResults { get; private set; } = false;
    [Sync] public GameObject Winner { get; private set; }
    [Sync] public TimeUntil ResultsTimer { get; private set; }

    public static VictoryManager Current { get; private set; }

    // Host-only guard so we only kick off the scene change once.
    private bool _hasFinishedResults = false;

    // Per-client: ensures the closing fade-out fires once during the results window.
    private bool _hasTriggeredResultsFade = false;

    // Per-client: starts the LerpTo on _victoryMusicHandle.Volume once the timer enters the window.
    private bool _hasTriggeredMusicFade = false;

    private SoundHandle _victoryMusicHandle;

    // Host-only: tiles queued to drop during results, sorted outward from the podium.
    private readonly List<(Tile tile, TimeUntil at)> _disintegrationSchedule = new();

    // Confetti bursts queued during results. Populated locally on every client when the
    // host's BroadcastBeginConfetti RPC arrives, then ticked locally to spawn prefab clones.
    // `initialVelocity` is per-burst world-space bias (inward toward the winner + upward)
    // applied to the spawned ParticleEffect so confetti arcs up and over the player.
    private readonly List<(TimeUntil at, Vector3 pos, Vector3 initialVelocity)> _confettiBurstSchedule = new();

    // Per-client: keeps the winner in a celebratory pose during results. Renderer animation
    // params are local-only (not network-synced), so each client sets the special_idle_states
    // enum on its own SkinnedModelRenderer.
    private const int WinnerIdleState = 1;  // citizen animgraph special_idle_states: 0=normal, 1=avatar_menu

    private static readonly Color[] ConfettiColors =
    {
        Color.Parse( "#FF5757" ) ?? Color.Red,
        Color.Parse( "#FFD93D" ) ?? Color.Yellow,
        Color.Parse( "#6BCB77" ) ?? Color.Green,
        Color.Parse( "#4D96FF" ) ?? Color.Blue,
        Color.Parse( "#FF6FFF" ) ?? Color.Magenta,
        Color.White,
    };

    protected override void OnEnabled()
    {
        Current = this;
    }

    protected override void OnDisabled()
    {
        // Stop the victory music handle so it doesn't carry over into the next scene
        if ( _victoryMusicHandle.IsValid() )
        {
            _victoryMusicHandle.Stop();
            _victoryMusicHandle = null;
        }

        if ( Current == this )
            Current = null;
    }

    protected override void OnFixedUpdate()
    {
        // Per-client: hold the winner in their victory pose regardless of host status.
        TickWinnerPose();
        // Per-client: drive the confetti schedule locally (populated by BroadcastBeginConfetti).
        TickConfettiBursts();

        // Per-client: trigger the closing screen fade once the results timer enters the fade window.
        if ( IsShowingResults && !_hasTriggeredResultsFade && (float)ResultsTimer <= ResultsFadeDuration )
        {
            _hasTriggeredResultsFade = true;
            ScreenFade.FadeOut( ResultsFadeDuration );
        }

        // Per-client: ramp the victory music down over the final stretch so the scene swap is more smooth
        // Mirrors the LerpTo pattern in Music.FadeOut — trigger once on entering the window, then
        // lerp every tick until the handle drops out / scene swaps.
        if ( IsShowingResults && !_hasTriggeredMusicFade && (float)ResultsTimer <= VictoryMusicFadeDuration )
        {
            _hasTriggeredMusicFade = true;
        }
        if ( _hasTriggeredMusicFade && _victoryMusicHandle.IsValid() )
        {
            _victoryMusicHandle.Volume = _victoryMusicHandle.Volume.LerpTo( 0f, Time.Delta / VictoryMusicFadeDuration );
        }

        if ( !Networking.IsHost ) return;
        if ( !IsShowingResults ) return;

        TickDisintegration();

        if ( !_hasFinishedResults && ResultsTimer <= 0f )
        {
            _hasFinishedResults = true;
            FinishGame();
        }
    }

    /// <summary>
    /// Host-only. Enter the post-game results phase: declare the winner, freeze the game,
    /// set up the podium tile, and start the timer that eventually swaps back to the lobby scene.
    /// <paramref name="winner"/> may be null (e.g. last two players were eliminated simultaneously).
    /// </summary>
    public void BeginVictory( PlayerController winner )
    {
        if ( !Networking.IsHost ) return;
        if ( IsShowingResults ) return;

        GameObject winnerGameObject = winner.IsValid() ? winner.GameObject : null;

        GameObject podiumGameObject = null;
        if ( winnerGameObject.IsValid() )
        {
            podiumGameObject = SetupPodium( winner );

            // Center the winner on the podium tile so a stray last step can't carry them off the edge.
            // Teleport must run while the rigidbody is still active so the position
            // change propagates through physics/network sync before the freeze lands.
            if ( podiumGameObject.IsValid() )
                BroadcastTeleportWinner( winnerGameObject, podiumGameObject.WorldPosition );

            BroadcastFreezeWinner( winnerGameObject );
        }

        ScheduleDisintegration( podiumGameObject );

        BroadcastResultsBegin( winnerGameObject );

        // Session-wide leaderboard: fanned out so every client increments
        // their own local Leaderboard copy.
        ulong winnerSteamId = winner?.Network?.Owner?.SteamId ?? 0UL;
        if ( winnerSteamId != 0UL )
        {
            BroadcastRecordWin( winnerSteamId );
        }

        // Confetti: fan out via RPC so every client populates its own local burst schedule.
        // [Sync] doesn't propagate reliably on this scene-singleton (verified), but
        // [Rpc.Broadcast] bodies do run on clients (same mechanism BroadcastResultsBegin uses).
        if ( winnerGameObject.IsValid() && podiumGameObject.IsValid() )
        {
            Vector3 winnerForward = winnerGameObject.WorldRotation.Forward.WithZ( 0f );
            if ( winnerForward.LengthSquared > 0.001f )
            {
                BroadcastBeginConfetti( podiumGameObject.WorldPosition, winnerForward.Normal );
            }
        }

        string winnerName = winner?.Network?.Owner?.DisplayName ?? "Unknown";
        Log.Info( $"{winnerName} won! Showing results for {ResultsDuration}s." );
    }

    // Find the tile the winner is standing on and convert it into a golden podium. If they
    // were mid-air, spawn a fresh podium tile above the arena center and teleport them onto it.
    // Returns the podium tile's prefab root so callers can exclude it from the disintegration wave.
    private GameObject SetupPodium( PlayerController winner )
    {
        Vector3 winnerPos = winner.WorldPosition;
        // Start well above the player so we're clearly outside their body capsule, trace down
        // past their feet. Ignore both the player root and the separate ColliderObject — the
        // "Colliders" child isn't tagged "player", so WithoutTags alone won't exclude it.
        var trace = Scene.Trace.Ray( winnerPos + Vector3.Up * 200f, winnerPos + Vector3.Down * 60f )
            .WithoutTags( "player" )
            .IgnoreGameObjectHierarchy( winner.GameObject );
        if ( winner.ColliderObject.IsValid() )
            trace = trace.IgnoreGameObjectHierarchy( winner.ColliderObject );
        SceneTraceResult result = trace.Run();

        GameObject podiumGameObject = null;
        if ( result.Hit && result.GameObject.IsValid() )
        {
            // result.GameObject is the TileModelCollider; one level up is the tile prefab root,
            // which contains the Tile component on a sibling child. Don't use .Root — that walks
            // all the way up to the scene-level TileManager and would tint the whole arena.
            GameObject tileRoot = result.GameObject.Parent;
            Tile existingTile = tileRoot?.GetComponentInChildren<Tile>();
            if ( existingTile != null )
            {
                podiumGameObject = tileRoot;
            }
        }

        if ( podiumGameObject == null )
        {
            // Mid-air winner: spawn a podium tile above the arena center. The winner is teleported
            // onto it by BeginVictory, not here, so the snap happens after the freeze.
            podiumGameObject = SpawnPodiumGameObject();
            if ( podiumGameObject == null )
            {
                Log.Warning( "[Results] Could not produce a podium tile for mid-air winner." );
                return null;
            }
        }

        BroadcastConvertToPodium( podiumGameObject );
        return podiumGameObject;
    }

    // Host-only. Build an outward-from-podium drop schedule for every non-podium tile, spread
    // across DisintegrationDuration. Tile.BreakTile is host-authoritative and flips a [Sync]
    // _falling flag, so clients animate the cascade for free.
    private void ScheduleDisintegration( GameObject podiumGameObject )
    {
        _disintegrationSchedule.Clear();
        if ( TileManager == null ) return;

        Vector3 podiumPos = podiumGameObject.IsValid()
            ? podiumGameObject.WorldPosition
            : TileManager.WorldPosition;

        var candidates = new List<(Tile tile, float distance)>();
        foreach ( Tile tile in TileManager.GameObject.GetComponentsInChildren<Tile>() )
        {
            if ( !tile.IsValid() ) continue;
            // Tile lives on a child of the prefab root; comparing parents skips the podium tile.
            if ( podiumGameObject.IsValid() && tile.GameObject.Parent == podiumGameObject ) continue;

            // Use horizontal distance only so every layer ripples outward together,
            // instead of cascading top-layer-first to bottom-layer-last.
            float horizontalDistance = (tile.WorldPosition - podiumPos).WithZ( 0f ).Length;
            candidates.Add( (tile, horizontalDistance) );
        }

        candidates.Sort( ( a, b ) => a.distance.CompareTo( b.distance ) );

        for ( int i = 0; i < candidates.Count; i++ )
        {
            float t = candidates.Count > 1 ? (float)i / (candidates.Count - 1) : 0f;
            TimeUntil dropAt = t * DisintegrationDuration;
            _disintegrationSchedule.Add( (candidates[i].tile, dropAt) );
        }
    }

    // Runs on every client. Holds the winner in the citizen animgraph's avatar_menu pose
    // (hands-on-hips victory stance) for the duration of the results window.
    private void TickWinnerPose()
    {
        if ( !IsShowingResults || !Winner.IsValid() ) return;

        PlayerController pc = Winner.GetComponent<PlayerController>( true );
        if ( pc == null || !pc.Renderer.IsValid() ) return;

        pc.Renderer.Set( "special_idle_states", WinnerIdleState );
    }

    // Fanned out from the host so every client (including host) populates its own local
    // confetti schedule from the same pattern. Plain RPC — [Sync] doesn't propagate on this
    // scene-singleton (verified). Mirror of the BroadcastResultsBegin pattern.
    [Rpc.Broadcast]
    private void BroadcastBeginConfetti( Vector3 podiumPos, Vector3 winnerForward )
    {
        _confettiBurstSchedule.Clear();

        if ( winnerForward.LengthSquared < 0.001f ) return;
        winnerForward = winnerForward.Normal;

        // (delay seconds, yaw offset from "directly behind" in degrees, radius, height)
        // Spawn well behind the winner and at or below podium level so each burst starts
        // near the bottom of the WinnerFocusCam frame, then arcs up into view.
        var pattern = new (float delay, float yaw, float radius, float height)[]
        {
            ( 0.00f,    0f, 110f,  -5f ),  // dead behind
			( 0.60f,  -55f, 120f,   0f ),  // behind-left
			( 1.20f,   55f, 120f,   0f ),  // behind-right
			( 2.00f,    0f,  95f,  10f ),  // closer, slightly higher (over-the-top pop)
			( 2.80f,  -90f, 135f, -10f ),  // hard left flank, low
			( 2.80f,   90f, 135f, -10f ),  // hard right flank, low
			( 3.80f,    0f, 110f,  -5f ),  // final center pop
		};

        foreach ( var (delay, yaw, radius, height) in pattern )
        {
            Vector3 offsetDir = Rotation.FromYaw( yaw ) * (-winnerForward);
            Vector3 pos = podiumPos + offsetDir * radius + Vector3.Up * height;

            // Push each particle horizontally toward the winner (so bursts behind/around
            // them arc inward) and upward (so they rise into the camera frame before
            // drifting back down like paper). World-space — the prefab has LocalSpace=0.
            Vector3 inwardHorizontal = (podiumPos - pos).WithZ( 0f );
            Vector3 inwardDir = inwardHorizontal.LengthSquared > 0.001f ? inwardHorizontal.Normal : Vector3.Zero;
            Vector3 initialVelocity = inwardDir * 260f + Vector3.Up * 240f;

            _confettiBurstSchedule.Add( (delay, pos, initialVelocity) );
        }
    }

    private void TickConfettiBursts()
    {
        if ( _confettiBurstSchedule.Count == 0 ) return;

        for ( int i = _confettiBurstSchedule.Count - 1; i >= 0; i-- )
        {
            var entry = _confettiBurstSchedule[i];
            if ( entry.at <= 0f )
            {
                SpawnConfettiLocally( entry.pos, entry.initialVelocity );
                _confettiBurstSchedule.RemoveAt( i );
            }
        }
    }

    private void TickDisintegration()
    {
        if ( _disintegrationSchedule.Count == 0 ) return;

        for ( int i = _disintegrationSchedule.Count - 1; i >= 0; i-- )
        {
            var entry = _disintegrationSchedule[i];
            if ( !entry.tile.IsValid() )
            {
                _disintegrationSchedule.RemoveAt( i );
                continue;
            }
            if ( entry.at <= 0f )
            {
                entry.tile.BreakTile();
                _disintegrationSchedule.RemoveAt( i );
            }
        }
    }

    private GameObject SpawnPodiumGameObject()
    {
        if ( TileManager == null || !TileManager.TilePrefab.IsValid() ) return null;

        // Raise the podium above the top layer so it doesn't z-fight with the existing center tile.
        Vector3 spawnPos = TileManager.WorldPosition + Vector3.Up * 96f;
        GameObject tileGameObject = TileManager.TilePrefab.Clone( new CloneConfig
        {
            Parent = TileManager.GameObject,
            StartEnabled = true,
            Transform = new Transform( spawnPos )
        } );
        tileGameObject.Name = "Tile_Podium";
        tileGameObject.NetworkSpawn();
        return tileGameObject;
    }

    // Fanned out so every client locally disables the regular Tile behavior on this GameObject
    // and swaps in a PodiumTile component (which tints it gold + resets any in-progress wobble).
    [Rpc.Broadcast]
    private void BroadcastConvertToPodium( GameObject tileGameObject )
    {
        if ( !tileGameObject.IsValid() ) return;

        // Tile lives on a child of the prefab root, so search downward.
        Tile tile = tileGameObject.GetComponentInChildren<Tile>();
        if ( tile != null )
        {
            tile.SetTriggerEnabled( false );
            tile.Enabled = false;
        }

        if ( tileGameObject.GetComponent<PodiumTile>() == null )
        {
            tileGameObject.AddComponent<PodiumTile>();
        }
    }

    // Fanned out so every client sets the winner's local PlayerController flags. Matches the
    // pattern used by PlayerManager.EnablePlayersInput.
    [Rpc.Broadcast]
    private void BroadcastFreezeWinner( GameObject winnerGameObject )
    {
        if ( !winnerGameObject.IsValid() ) return;
        PlayerController pc = winnerGameObject.GetComponent<PlayerController>();
        if ( pc == null ) return;
        pc.UseInputControls = false;
        pc.UseCameraControls = false;
        // Clear any held input — otherwise the last WishVelocity (e.g. W still pressed)
        // keeps driving the controller forward after input is disabled.
        pc.WishVelocity = Vector3.Zero;

        // Disable the controller and freeze rigidbody motion so the winner-hop tick can drive
        // the transform directly without the move modes or physics overriding our position.
        pc.Enabled = false;
        Rigidbody rb = winnerGameObject.GetComponent<Rigidbody>();
        if ( rb != null ) rb.MotionEnabled = false;

        // PlayerController.OnUpdate is what pumps animation params each frame; once we
        // disable it, the last "sprinting" values stick and the winner runs in place.
        // Zero them so the citizen animgraph falls back to idle.
        SkinnedModelRenderer renderer = pc.Renderer;
        if ( renderer.IsValid() )
        {
            renderer.Set( "move_groundspeed", 0f );
            renderer.Set( "move_x", 0f );
            renderer.Set( "move_y", 0f );
            renderer.Set( "move_z", 0f );
            renderer.Set( "move_direction", 0f );
            renderer.Set( "b_grounded", true );
        }
    }

    // Only the owning client actually moves the transform — it owns the player's authority.
    [Rpc.Broadcast]
    private void BroadcastTeleportWinner( GameObject winnerGameObject, Vector3 position )
    {
        if ( !winnerGameObject.IsValid() ) return;

        // Lock input on every client so a held WASD / camera input can't carry the
        // player off the podium during the one-frame gap before BroadcastFreezeWinner.
        PlayerController pc = winnerGameObject.GetComponent<PlayerController>();
        if ( pc != null )
        {
            pc.UseInputControls = false;
            pc.UseCameraControls = false;
            pc.WishVelocity = Vector3.Zero;
        }

        if ( !winnerGameObject.Network.IsOwner ) return;
        winnerGameObject.WorldPosition = position;
    }

    private void SpawnConfettiLocally( Vector3 spawnPos, Vector3 initialVelocity )
    {
        if ( !ConfettiPrefab.IsValid() ) return;

        // One clone per color so each burst contains a mix of colored particles. The clone's
        // ParticleSphereEmitter is single-shot (Loop=false, DestroyOnEnd=true) so each cleans itself up.
        foreach ( Color color in ConfettiColors )
        {
            GameObject clone = ConfettiPrefab.Clone( new CloneConfig
            {
                StartEnabled = true,
                Transform = new Transform( spawnPos, Rotation.Identity )
            } );

            ParticleEffect effect = clone.GetComponent<ParticleEffect>();
            if ( effect != null )
            {
                effect.Tint = color;
                effect.InitialVelocity = initialVelocity;
            }
        }

        if ( ConfettiSound == null ) return;
        SoundHandle handle = Sound.Play( ConfettiSound, spawnPos );
        if ( !handle.IsValid() ) return;
        handle.Volume = 0.2f;
    }

    // Fanned out from the host so every client (including host) sets the same local results
    // state and starts its own timer. Plain RPC instead of [Sync] because [Sync] props on this
    // scene-level component don't propagate to non-host clients in this project's setup.
    [Rpc.Broadcast]
    private void BroadcastResultsBegin( GameObject winnerGameObject )
    {
        IsShowingResults = true;
        Winner = winnerGameObject;
        ResultsTimer = ResultsDuration;

        // Fade out any in-scene music (the game track) so the victory cue can take over
        // cleanly. Music components live on dedicated GameObjects in the scene.
        foreach ( Music music in Scene.GetAllComponents<Music>() )
        {
            music.FadeOut();
        }

        if ( VictoryMusic != null )
        {
            _victoryMusicHandle = Sound.Play( VictoryMusic );
        }
    }

    private void FinishGame()
    {
        if ( !Networking.IsHost ) return;

        if ( SceneToLoadFinish is null )
        {
            Log.Error( "VictoryManager: SceneToLoadFinish is not assigned, can't return to lobby." );
            return;
        }

        BroadcastLoadLobbyScene( SceneToLoadFinish.ResourcePath );
    }

    // Per-client Scene.Load to reload the lobby.
    // Game.ChangeScene was tried first but wouldn't suppress the loading screen.
    [Rpc.Broadcast]
    private void BroadcastLoadLobbyScene( string resourcePath )
    {
        SceneLoadOptions loadOptions = new();
        if ( !loadOptions.SetScene( resourcePath ) ) return;
        loadOptions.ShowLoadingScreen = false;
        Game.ActiveScene.Load( loadOptions );
    }

    [Rpc.Broadcast]
    private void BroadcastRecordWin( ulong steamId )
    {
        Leaderboard.RecordWin( steamId );
    }
}
