using System.Linq;
using Sandbox;

/// <summary>
/// Lobby interact button. Look at it and press Use to bump
/// <see cref="Bots.DesiredCount"/> by <see cref="Delta"/>.
/// </summary>
public sealed class LobbyBotButton : Component
{
    [Property] public int Delta { get; set; } = 1;

    /// <summary>Max distance from the local player's camera to the button before it responds.</summary>
    [Property] public float MaxInteractDistance { get; set; } = 250f;

    protected override void OnUpdate()
    {
        if ( !Input.Pressed( "Use" ) ) return;

        CameraComponent camera = Scene.Camera;
        if ( camera == null ) return;

        Vector3 from = camera.WorldPosition;
        Vector3 to = from + camera.WorldRotation.Forward * MaxInteractDistance;

        // Ignore the local player so their body (in third-person view the camera
        // sits behind them) doesn't block the ray before it reaches the button.
        var trace = Scene.Trace.Ray( from, to );
        GameObject localPlayer = Scene.GetAllComponents<PlayerController>()
            .FirstOrDefault( pc => pc.Network.IsOwner )?.GameObject;
        if ( localPlayer.IsValid() )
            trace = trace.IgnoreGameObjectHierarchy( localPlayer );

        SceneTraceResult result = trace.Run();
        if ( !result.Hit ) return;

        // Only respond if the ray landed on THIS button's GameObject (or a child
        // of it). Comparing roots would let sibling buttons under a shared parent
        // respond to each other's hits.
        if ( !IsSelfOrDescendant( result.GameObject ) ) return;

        BroadcastAdjust( Delta );
    }

    private bool IsSelfOrDescendant( GameObject other )
    {
        for ( GameObject cursor = other; cursor.IsValid(); cursor = cursor.Parent )
            if ( cursor == GameObject ) return true;
        return false;
    }

    // Fan the change to every client so Bots.DesiredCount stays in sync everywhere,
    // then let the host reconcile the actual spawn list.
    [Rpc.Broadcast]
    private void BroadcastAdjust( int delta )
    {
        Log.Info( $"[LobbyBotButton] BroadcastAdjust received. delta={delta} isHost={Networking.IsHost} before={Bots.DesiredCount}" );
        Bots.ChangeCount( delta );
        Log.Info( $"[LobbyBotButton] After Adjust, DesiredCount={Bots.DesiredCount}" );
        if ( !Networking.IsHost ) return;

        BotManager botManager = Scene.GetAllComponents<BotManager>().FirstOrDefault();
        Log.Info( $"[LobbyBotButton] BotManager found={botManager != null}, calling SyncBotCount." );
        botManager?.SyncBotCount();
    }
}
