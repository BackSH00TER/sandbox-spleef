using System;

public sealed class Tile : Component, Component.ITriggerListener
{
	// Timing + physics for the break sequence (delay before breaking, how long the
	// debris falls before despawn, mass applied so it actually falls, and the
	// flash pulse rate as the tile is about to give way).
	[Property, Group( "Break" )] public float BreakDelay { get; set; } = 1.0f;
	[Property, Group( "Break" )] public float FallDuration { get; set; } = 2.0f;
	[Property, Group( "Break" )] public float FallMass { get; set; } = 100f;
	[Property, Group( "Break" )] public float FlashSpeed { get; set; } = 12f;

	// Pre-break wobble: max roll angle in degrees and how fast it oscillates
	// (radians/sec fed into Sin).
	[Property, Group( "Wobble" )] public float WobbleAngle { get; set; } = 5.0f;
	[Property, Group( "Wobble" )] public float WobbleSpeed { get; set; } = 25.0f;

	// Visual dip when a player is standing on the tile: how far down (local units)
	// and how fast the model lerps toward depressed/rest.
	[Property, Group( "Depress" )] public float DepressDepth { get; set; } = 3f;
	[Property, Group( "Depress" )] public float DepressSpeed { get; set; } = 25f;

	// Wired up in the prefab inspector. SolidCollider is what the player stands on
	// (flipped to a trigger on break). TriggerCollider detects step-on. Model is the
	// renderer used for flash + depression bob (must be a child so we can move it
	// without moving the colliders). TileRoot is the prefab root we destroy on fall.
	[Property, Group( "References" )] public Collider SolidCollider { get; set; }
	[Property, Group( "References" )] public Collider TriggerCollider { get; set; }
	[Property, Group( "References" )] public ModelRenderer Model { get; set; }
	[Property, Group( "References" )] public GameObject TileRoot { get; set; }

	// Synced state used for host/client coordination.
	[Sync] private bool _triggered { get; set; } = false;
	[Sync] private bool _falling { get; set; } = false;

	/// <summary>True while the tile is a valid landing spot (not counting down to break, not falling).</summary>
	public bool IsSafe => !_triggered && !_falling;

	// The per-layer color the host assigns at grid build time. Synced so non-host
	// clients see the layer colors instead of the prefab default. Default is the zero
	// color (alpha 0); we use that as the "not assigned" sentinel.
	[Sync, Change( nameof( OnLayerTintChanged ) )] public Color LayerTint { get; set; }

	private TimeUntil _breakAt;
	private TimeUntil _destroyAt;
	private Rotation _restRotation;
	private Vector3 _modelRestPosition;
	private bool _hasCapturedModelRest;
	private bool _appliedBreakLocally = false;
	private int _playersOnTile = 0;
	private Color _baseTint = Color.White;
	private float _wobblePhase;
	private float _wobbleSpeedJitter = 1f;
	private Rigidbody _rigidbody;

	protected override void OnStart()
	{
		// Cache the initial rotation and build a deterministic random wobble offset.
		_restRotation = TileRoot.IsValid() ? TileRoot.WorldRotation : WorldRotation;
		var rng = new Random( HashCode.Combine( GameObject.Id ) );
		_wobblePhase = (float)rng.NextDouble() * MathF.PI * 2f;
		_wobbleSpeedJitter = 0.75f + (float)rng.NextDouble() * 0.5f;

		if ( Model.IsValid() )
		{
			CaptureModelRest();
			// If the host has already assigned a layer tint, apply it before capturing
			// the rest color so the flash animation pulses between the tint and white.
			if ( LayerTint.a > 0f )
				Model.Tint = LayerTint;
			_baseTint = Model.Tint;
		}

		// Start with the trigger disabled; the tile becomes active only when requested.
		if ( TriggerCollider.IsValid() )
		{
			TriggerCollider.Enabled = false;
		}
	}

	protected override void OnFixedUpdate()
	{
		// Host handles authoritative break timing and destruction.
		if ( Networking.IsHost )
		{
			HostFixedUpdate();
		}

		// All clients handle local animation and state application.
		ClientFixedUpdate();
	}

	private void HostFixedUpdate()
	{
		if ( _triggered && !_falling )
		{
			UpdateTriggeredAnimation();

			if ( _breakAt <= 0 )
			{
				BreakTile();
			}
		}

		if ( _falling && _destroyAt <= 0 )
		{
			TileRoot?.Destroy();
		}
	}

	private void ClientFixedUpdate()
	{
		if ( _triggered && !_falling )
		{
			UpdateTriggeredAnimation();
		}

		if ( Model.IsValid() && !_falling )
		{
			// Smoothly depress the tile when players are standing on it.
			var target = _playersOnTile > 0 ? _modelRestPosition + Vector3.Down * DepressDepth : _modelRestPosition;
			Model.LocalPosition = Vector3.Lerp( Model.LocalPosition, target, Time.Delta * DepressSpeed );
		}

		if ( _falling && !_appliedBreakLocally )
		{
			ApplyBreakStateLocally();
		}
	}

	private void UpdateTriggeredAnimation()
	{
		float progress = MathX.Clamp( 1f - (float)_breakAt / BreakDelay, 0f, 1f );
		float angle = MathF.Sin( Time.Now * WobbleSpeed * _wobbleSpeedJitter + _wobblePhase ) * WobbleAngle * progress;
		var wobbleTarget = TileRoot.IsValid() ? TileRoot : GameObject;
		wobbleTarget.WorldRotation = _restRotation * Rotation.FromRoll( angle );

		if ( Model.IsValid() )
		{
			float pulse = (MathF.Sin( Time.Now * FlashSpeed ) + 1f) * 0.5f;
			Model.Tint = Color.Lerp( _baseTint, Color.White, pulse * progress );
		}
	}

	public void OnTriggerEnter( Collider other )
	{
		if ( !other.Tags.Has( "player" ) ) return;

		_playersOnTile++;

		// Only the host can begin the break countdown.
		if ( !Networking.IsHost || _triggered || _falling ) return;

		_triggered = true;
		_breakAt = BreakDelay;
	}

	public void OnTriggerExit( Collider other )
	{
		if ( !other.Tags.Has( "player" ) ) return;

		_playersOnTile = Math.Max( 0, _playersOnTile - 1 );
	}

	public void SetTriggerEnabled( bool enabled )
	{
		if ( TriggerCollider.IsValid() )
		{
			TriggerCollider.Enabled = enabled;
		}
	}

	// Snap the model back to its captured rest position, cancelling any in-progress
	// depression bob. Useful when the tile is taken out of the normal break flow and
	// the lerp in ClientFixedUpdate stops running.
	public void SnapModelToRest()
	{
		if ( !Model.IsValid() ) return;
		// PodiumTile spawns a fresh tile and disables this component in the same frame,
		// so OnStart may never have run — capture the prefab-default rest position now
		// or the model snaps to (0,0,0) and rises above the collider top.
		CaptureModelRest();
		Model.LocalPosition = _modelRestPosition;
	}

	private void CaptureModelRest()
	{
		if ( _hasCapturedModelRest ) return;
		if ( !Model.IsValid() ) return;
		_modelRestPosition = Model.LocalPosition;
		_hasCapturedModelRest = true;
	}

	// Fires on clients when the host assigns LayerTint after the tile has already started.
	private void OnLayerTintChanged( Color oldValue, Color newValue )
	{
		if ( newValue.a <= 0f ) return;
		if ( Model.IsValid() )
			Model.Tint = newValue;
		_baseTint = newValue;
	}

	public void BreakTile()
	{
		if ( !Networking.IsHost || _falling ) return;

		_falling = true;
		_destroyAt = FallDuration;
		ApplyBreakStateLocally();
	}

	private void ApplyBreakStateLocally()
	{
		if ( _appliedBreakLocally ) return;
		_appliedBreakLocally = true;

		// Convert the walkable collider into a trigger so the player falls through.
		if ( SolidCollider.IsValid() )
			SolidCollider.IsTrigger = true;

		if ( TriggerCollider.IsValid() )
			TriggerCollider.Enabled = false;

		if ( SolidCollider.IsValid() )
		{
			_rigidbody = SolidCollider.GameObject.AddComponent<Rigidbody>();
			_rigidbody.MassOverride = FallMass;
			_rigidbody.MotionEnabled = true;
		}
	}
}
