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

    /// <summary>Radius of the interact ray. Bigger = more forgiving aim, but neighbouring objects steal hits sooner.</summary>
    [Property] public float InteractRadius { get; set; } = 10f;

    /// <summary>Optional visual child that gets pressed inward on click. Must be a child so moving it doesn't move the collider.</summary>
    [Property, Group( "Press Animation" )] public GameObject Model { get; set; }

    /// <summary>Local-space offset applied to the model while pressed. Tweak per-button so the visual moves "into" the panel — e.g. (0,-3,0) for a button facing +Y.</summary>
    [Property, Group( "Press Animation" )] public Vector3 PressOffset { get; set; } = new Vector3( 0f, -3f, 0f );

    /// <summary>Lerp speed toward pressed/rest. Higher = snappier.</summary>
    [Property, Group( "Press Animation" )] public float PressSpeed { get; set; } = 30f;

    /// <summary>How long the button stays depressed before returning.</summary>
    [Property, Group( "Press Animation" )] public float PressHoldDuration { get; set; } = 0.12f;

    private Vector3 _modelRestPosition;
    private bool _hasCapturedRest;
    private TimeUntil _pressReleaseAt;

    protected override void OnStart()
    {
        CaptureRest();
        _pressReleaseAt = 0f;
    }

    protected override void OnUpdate()
    {
        UpdatePressAnimation();

        if ( !Input.Pressed( "Use" ) ) return;

        CameraComponent camera = Scene.Camera;
        if ( camera == null )
        {
            Log.Info( "[LobbyBotButton] Use pressed but Scene.Camera is null." );
            return;
        }

        Vector3 from = camera.WorldPosition;
        Vector3 to = from + camera.WorldRotation.Forward * MaxInteractDistance;

        // Raycast to see if player hits the button, ignore the player collider
        // Uses a radius to make the aiming a bit more forgiving
        SceneTraceResult result = Scene.Trace.Ray( from, to )
            .Size( InteractRadius )
            .WithoutTags( "player" )
            .Run();
        if ( !result.Hit )
        {
            Log.Info( $"[LobbyBotButton {GameObject.Name}] Use pressed but ray missed. from={from} dir={camera.WorldRotation.Forward}" );
            return;
        }

        // Only respond if the ray landed on THIS button's GameObject (or a child of it).
        if ( !IsSelfOrDescendant( result.GameObject ) )
        {
            Log.Info( $"[LobbyBotButton {GameObject.Name}] Use pressed, ray hit '{result.GameObject.Name}' but not us." );
            return;
        }

        BroadcastAdjust( Delta );
    }

    // Animates the button depress on use
    private void UpdatePressAnimation()
    {
        if ( !Model.IsValid() ) return;
        CaptureRest();

        Vector3 target = _pressReleaseAt > 0f
            ? _modelRestPosition + PressOffset
            : _modelRestPosition;
        Model.LocalPosition = Vector3.Lerp( Model.LocalPosition, target, Time.Delta * PressSpeed );
    }

    private void CaptureRest()
    {
        if ( _hasCapturedRest ) return;
        if ( !Model.IsValid() ) return;
        _modelRestPosition = Model.LocalPosition;
        _hasCapturedRest = true;
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
        Bots.ChangeCount( delta );
        _pressReleaseAt = PressHoldDuration;

        if ( !Networking.IsHost ) return;
        Scene.GetAllComponents<BotManager>().FirstOrDefault()?.SyncBotCount();
    }
}
