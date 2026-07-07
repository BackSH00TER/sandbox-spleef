using System;
using System.Numerics;
using System.Runtime.InteropServices.Swift;
using Sandbox;
using Sandbox.ui;

public sealed class PlayerLeap : Component, PlayerController.IEvents
{
	[Property] PlayerController TargetController { get; set; }
	[Property] GameObject TargetBody { get; set; }
	[Property] SkinnedModelRenderer TargetRenderer { get; set; }

	public bool IsLeaping = false;
	public float LeapCooldown = 3f;
	public float LeapCooldownTime = 0f;

	// Captured on BeginLeap so FinishLeap can restore whatever state the controller
	// had before the leap (bots start input-suppressed and should stay that way).
	private bool _restoreInputControls;

	protected override void OnStart()
	{
		if ( !Network.IsOwner ) return;
		// Host owns bots but is also the local player; without this guard every bot
		// would spawn its own on-screen control hint on the host.
		if ( this.IsBot() ) return;
		AddUI();
	}

	protected override void OnUpdate()
	{
		if ( !Network.IsOwner ) return;
		if ( IsLeaping ) return;

		LeapCooldownTime -= Time.Delta;
		if ( LeapCooldownTime < 0.01 && Input.Pressed( "attack1" ) && TargetController.UseInputControls )
		{
			BeginLeap();
		}
	}

	public void AddUI()
	{
		GameObject gameObject = new GameObject( "PlayerControlsUI" );
		gameObject.NetworkMode = NetworkMode.Never;
		gameObject.AddComponent<ScreenPanel>();
		var ui = gameObject.AddComponent<PlayerControlsUI>();
		ui.PlayerLeap = this;
	}

	[Rpc.Broadcast]
	public void BeginLeap()
	{
		// Set state
		IsLeaping = true;
		LeapCooldownTime = LeapCooldown;

		// Update properties
		// Capture the current input state so we can restore it after the leap. (bots start input-suppressed and should stay that way)
		_restoreInputControls = TargetController.UseInputControls;
		TargetController.UseInputControls = false;
		TargetRenderer.Set( "special_movement_states", 2 );

		// Get movement variables
		Rigidbody rb = TargetController.GetComponent<Rigidbody>();
		Vector3 upVelocity = Vector3.Up * 400f;
		Vector3 flatEyeAngle = new Vector3( TargetController.EyeAngles.Forward.x, TargetController.EyeAngles.Forward.y, 0 );
		Vector3 forwardVelocity = flatEyeAngle.Normal * 400f;

		// Apply movement
		TargetController.Jump( upVelocity );
		rb.Velocity = upVelocity + forwardVelocity;
		TargetBody.WorldRotation = Rotation.LookAt( forwardVelocity );
	}

	void PlayerController.IEvents.OnLanded( float distance, Vector3 impactVelocity )
	{
		if ( IsLeaping ) FinishLeap();
	}

	private void FinishLeap()
	{
		IsLeaping = false;
		TargetRenderer.Set( "special_movement_states", 0 );
		TargetController.UseInputControls = _restoreInputControls;
	}
}
