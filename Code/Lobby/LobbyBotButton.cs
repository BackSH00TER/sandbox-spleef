using System.Linq;
using Sandbox;
using Sandbox.Rendering;

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
    [Property, InputAction] public string InteractAction { get; set; } = "Use";

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

        CameraComponent camera = Scene.Camera;
        if ( camera == null )
        {
            Log.Info( "[LobbyBotButton] Use pressed but Scene.Camera is null." );
            return;
        }

        Vector3 from = camera.WorldPosition;
        Vector3 to = from + camera.WorldRotation.Forward * MaxInteractDistance;

        // Raycast to see if player is looking at the button, ignore the player collider
        // Uses a radius to make the aiming a bit more forgiving
        SceneTraceResult result = Scene.Trace.Ray( from, to )
            .Size( InteractRadius )
            .WithoutTags( "player" )
            .Run();
        if ( !result.Hit )
        {
            return;
        }

        // Only respond if the ray landed on THIS button's GameObject (or a child of it).
        if ( !IsSelfOrDescendant( result.GameObject ) )
        {
            return;
        }

        ShowInteractPrompt();

        // Handle input
        if ( Input.Pressed( InteractAction ) )
        {
            BroadcastAdjust( Delta );
        }
    }

    // Paint HUD with the interact glyph and action name
    private void ShowInteractPrompt()
    {
        // Set universal layout properties
        int marginBottom = 120;

        // Set background rectangle properties
        int bgRectWidth = 120;
        int bgRectHeight = 60;
        int bgBorderRadius = 15;
        var bgRect = new Rect(
            (Screen.Size.x / 2) - (bgRectWidth / 2),
            (Screen.Size.y - marginBottom) - (bgRectHeight / 2),
            bgRectWidth, bgRectHeight );

        // Set glyph properties
        var glyphSize = new Vector2( 40f, 40f );
        var glyphTexture = Input.GetGlyph( InteractAction, InputGlyphSize.Medium, false );
        var glyphRect = new Rect(
            (Screen.Size.x / 2) - (bgRectWidth / 2) + 10f,
            (Screen.Size.y - marginBottom) - (bgRectHeight / 2) + 10f,
            glyphSize.x, glyphSize.y
        );

        // Set text properties
        int textSize = 20;
        int textWeight = 700;
        string textFont = "Poppins";
        Color textColor = Color.White;

        // Paint HUD
        HudPainter hud = Scene.Camera.Hud;
        hud.DrawRect( bgRect, new Color( 0f, 0f, 0f, 0.6f ), bgBorderRadius );
        hud.DrawTexture( glyphTexture, glyphRect );
        hud.DrawText(
            new TextRendering.Scope( InteractAction, textColor, textSize, textFont ) { FontWeight = textWeight },
            new Vector2(
                (Screen.Size.x / 2) + (glyphRect.Size.x / 2),
                (Screen.Size.y - marginBottom) ),
            TextFlag.Center
        );
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
