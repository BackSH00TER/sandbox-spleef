using System;

/// <summary>
/// Free-orbit spectator camera. Lives on the main camera GameObject. Entered via
/// <see cref="Activate"/> when the local player is eliminated: free mouse-look orbit,
/// LMB/RMB cycles between remaining players. Bails while the results phase is up so
/// <see cref="WinnerFocusCam"/> can drive the camera instead.
/// </summary>
public sealed class SpectatorMode : Component
{
    /// <summary>Scene-level singleton so other components (GameManager, HUD) can find us.</summary>
    public static SpectatorMode Current { get; private set; }

    /// <summary>True once the local client has entered spectate mode.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Display name of the player currently being spectated, or null.</summary>
    public string SpectatingName => _spectatedPlayer.IsValid() ? _spectatedPlayer.GetPlayerName() : null;

    private PlayerController _spectatedPlayer;
    private int _spectatedIndex;

    // Orbit angles around the spectated player, driven by mouse look.
    private Angles _orbitAngles = new( 20f, 0f, 0f );
    private const float OrbitDistance = 220f;
    private const float OrbitHeight = 90f;
    private const float MinPitch = -80f;
    private const float MaxPitch = 80f;

    protected override void OnEnabled()
    {
        Current = this;
    }

    protected override void OnDisabled()
    {
        if ( Current == this )
            Current = null;
    }

    /// <summary>
    /// Enter spectate mode on this client. Idempotent — extra calls are no-ops.
    /// </summary>
    public void Activate()
    {
        if ( IsActive ) return;
        IsActive = true;

        var targets = GetAvailableTargets();
        if ( targets.Count > 0 )
        {
            _spectatedIndex = 0;
            _spectatedPlayer = targets[0];
        }
    }

    protected override void OnUpdate()
    {
        if ( !IsActive ) return;
        // Results phase: hand the camera over to WinnerFocusCam.
        if ( VictoryManager.Current?.IsShowingResults == true ) return;

        var targets = GetAvailableTargets();
        if ( targets.Count == 0 )
        {
            _spectatedPlayer = null;
            return;
        }

        // The player we were watching is gone: roll forward to the next one.
        if ( !_spectatedPlayer.IsValid() || !targets.Contains( _spectatedPlayer ) )
        {
            _spectatedIndex %= targets.Count;
            _spectatedPlayer = targets[_spectatedIndex];
        }

        if ( Input.Pressed( "Attack1" ) )
        {
            _spectatedIndex = (_spectatedIndex - 1 + targets.Count) % targets.Count;
            _spectatedPlayer = targets[_spectatedIndex];
        }
        else if ( Input.Pressed( "Attack2" ) )
        {
            _spectatedIndex = (_spectatedIndex + 1) % targets.Count;
            _spectatedPlayer = targets[_spectatedIndex];
        }

        UpdateCamera();
    }

    private List<PlayerController> GetAvailableTargets()
    {
        // All remaining players in the scene. Eliminated players are destroyed by
        // PlayerManager so they naturally drop out of this list.
        return Scene.GetAllComponents<PlayerController>()
            .Where( p => p.IsValid() )
            .ToList();
    }

    private void UpdateCamera()
    {
        if ( !_spectatedPlayer.IsValid() ) return;

        var camera = Scene.Camera;
        if ( !camera.IsValid() ) return;

        var look = Input.AnalogLook;
        _orbitAngles.yaw += look.yaw;
        _orbitAngles.pitch = Math.Clamp( _orbitAngles.pitch + look.pitch, MinPitch, MaxPitch );

        var lookAt = _spectatedPlayer.WorldPosition + Vector3.Up * 60f;
        var orbitRotation = _orbitAngles.ToRotation();
        // Sit OrbitDistance units behind the orbit's forward direction, then lift by OrbitHeight for a slight top-down angle.
        var camPos = lookAt + orbitRotation.Forward * -OrbitDistance + Vector3.Up * OrbitHeight;

        camera.WorldPosition = camPos;
        camera.WorldRotation = Rotation.LookAt( (lookAt - camPos).Normal );
    }
}
