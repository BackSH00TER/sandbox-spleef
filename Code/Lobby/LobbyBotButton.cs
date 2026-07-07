using System.Linq;
using Sandbox;

/// <summary>
/// Lobby interact button. Look at it and press Use to bump
/// <see cref="Bots.DesiredCount"/> by <see cref="Delta"/>.
/// </summary>
public sealed class LobbyBotButton : Component, IInteractable
{
    [Property] public int Delta { get; set; } = 1;

    [Property, InputAction] public string InteractAction { get; set; } = "Use";

    /// <summary>Label shown next to the input glyph on the HUD prompt.</summary>
    [Property] public string InteractPrompt { get; set; } = "Use";

    /// <summary>Optional visual child that gets pressed inward on click. Must be a child so moving it doesn't move the collider.</summary>
    [Property, Group( "Press Animation" )] public GameObject Model { get; set; }

    /// <summary>Local-space offset applied to the model while pressed. Tweak per-button so the visual moves "into" the panel, e.g. (0,-3,0) for a button facing +Y.</summary>
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
    }

    public void OnInteract( GameObject interactor )
    {
        BroadcastAdjust( Delta );
    }

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
