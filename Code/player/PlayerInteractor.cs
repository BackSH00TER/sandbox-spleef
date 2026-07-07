using Sandbox;
using Sandbox.Rendering;

/// <summary>
/// Runs on the local player each frame: traces forward from the camera, finds any
/// component implementing <see cref="IInteractable"/> on the hit hierarchy, paints
/// a HUD prompt for it, and fires OnInteract when the player presses its action.
/// </summary>
public sealed class PlayerInteractor : Component
{
    /// <summary>How close the player (not the camera) must be to the point they're aiming at to interact. In third person the camera sits well behind the player, so gating on camera distance would be misleading.</summary>
    [Property] public float MaxInteractDistance { get; set; } = 200f;

    /// <summary>Draw the interact trace with DebugOverlay each frame.</summary>
    [Property, Group( "Debug" )] public bool ShowInteractGizmo { get; set; } = false;

    private PlayerController _playerController;

    protected override void OnStart()
    {
        // Interaction is a lobby-only concern (bot buttons). No point tracing every frame in-game.
        if ( LobbyManager.Current == null ) Enabled = false;
    }

    protected override void OnUpdate()
    {
        if ( GameObject.Network.Owner != Connection.Local ) return;

        CameraComponent camera = Scene.Camera;
        if ( camera == null ) return;

        // Trace has to be long enough to reach MaxInteractDistance past the player,
        // since the camera can sit way behind the player in third person.
        Vector3 from = camera.WorldPosition;
        float rayLength = Vector3.DistanceBetween( from, WorldPosition ) + MaxInteractDistance;
        Vector3 to = from + camera.WorldRotation.Forward * rayLength;

        SceneTraceResult result = Scene.Trace.Ray( from, to )
            .IgnoreGameObjectHierarchy( GameObject )
            .Run();

        // IgnoreGameObjectHierarchy misses runtime-spawned clothing/body parts
        // that end up under our root but weren't part of the prefab tree at trace-filter build time.
        // We ARE the root, so root-equality here is unambiguous (no sibling-component confusion).
        bool didHitSelf = result.Hit && result.GameObject.IsValid() && result.GameObject.Root == GameObject;
        if ( didHitSelf ) result = default;

        IInteractable interactable = result.Hit ? FindInteractable( result.GameObject ) : null;

        // Range check is player-to-hit, so the button responds based on where the player is standing,
        // not where the camera is floating.
        bool isInRange = interactable != null
            && Vector3.DistanceBetween( WorldPosition, result.HitPosition ) <= MaxInteractDistance;

        if ( ShowInteractGizmo ) DrawInteractDebug( to, result, isInRange );

        if ( !isInRange ) return;

        ShowInteractPrompt( interactable );

        if ( Input.Pressed( interactable.InteractAction ) )
        {
            interactable.OnInteract( GameObject );
        }
    }

    // Walk up from the hit game object looking for the nearest IInteractable.
    private static IInteractable FindInteractable( GameObject hit )
    {
        for ( GameObject cursor = hit; cursor.IsValid(); cursor = cursor.Parent )
        {
            foreach ( Component component in cursor.Components.GetAll( FindMode.EverythingInSelf ) )
            {
                if ( component is IInteractable interactable ) return interactable;
            }
        }
        return null;
    }

    private void ShowInteractPrompt( IInteractable interactable )
    {
        int marginBottom = 120;
        int bgRectWidth = 120;
        int bgRectHeight = 60;
        int bgBorderRadius = 15;
        var bgRect = new Rect(
            (Screen.Size.x / 2) - (bgRectWidth / 2),
            (Screen.Size.y - marginBottom) - (bgRectHeight / 2),
            bgRectWidth, bgRectHeight );

        var glyphSize = new Vector2( 40f, 40f );
        var glyphTexture = Input.GetGlyph( interactable.InteractAction, InputGlyphSize.Medium, false );
        var glyphRect = new Rect(
            (Screen.Size.x / 2) - (bgRectWidth / 2) + 10f,
            (Screen.Size.y - marginBottom) - (bgRectHeight / 2) + 10f,
            glyphSize.x, glyphSize.y
        );

        int textSize = 20;
        int textWeight = 700;
        string textFont = "Poppins";
        Color textColor = Color.White;

        HudPainter hud = Scene.Camera.Hud;
        hud.DrawRect( bgRect, new Color( 0f, 0f, 0f, 0.6f ), bgBorderRadius );
        hud.DrawTexture( glyphTexture, glyphRect );
        hud.DrawText(
            new TextRendering.Scope( interactable.InteractPrompt, textColor, textSize, textFont ) { FontWeight = textWeight },
            new Vector2(
                (Screen.Size.x / 2) + (glyphRect.Size.x / 2),
                (Screen.Size.y - marginBottom) ),
            TextFlag.Center
        );
    }

    // Draws from the player's eye position so third-person perspective doesn't
    // obscure where the ray is coming from. Overlay-rendered so it draws through walls.
    // green = interactable in range, yellow = hit something else (or out of range), gray = miss.
    private void DrawInteractDebug( Vector3 fallbackEnd, SceneTraceResult result, bool didHitInteractableInRange )
    {
        Vector3 endPoint = result.Hit ? result.HitPosition : fallbackEnd;

        Color color = didHitInteractableInRange ? Color.Green
            : result.Hit ? Color.Yellow
            : Color.Gray;

        _playerController ??= GetComponent<PlayerController>();
        Vector3 start = _playerController != null ? _playerController.EyePosition + Vector3.Down * 25f : WorldPosition;

        DebugOverlay.Line( start, endPoint, color, overlay: true );
        DebugOverlay.Sphere( new Sphere( endPoint, 3f ), color, overlay: true );
    }
}
