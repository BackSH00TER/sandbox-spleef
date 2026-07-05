using Sandbox;
using Sandbox.Physics;

public sealed class PlayerGrab : Component
{
	[Property] public PlayerController TargetController { get; set; }
	[Property] public SkinnedModelRenderer TargetRenderer { get; set; }
	[Property, InputAction] public string GrabInput { get; set; } = "attack2";
	public float GrabRange { get; private set; } = 350f;

	private Rigidbody _grabbedBody = null;
	private PhysicsJoint _grabJoint = null;


	protected override void OnUpdate()
	{
		if ( Input.Down( GrabInput ) )
		{
			_grabbedBody ??= TryGrab();
		}

		if ( Input.Released( GrabInput ) )
		{
			ReleaseGrab();
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( _grabbedBody != null )
		{
			ApplyGrabForce();
		}
	}


	private Rigidbody TryGrab()
	{
		// Animation
		TargetRenderer.Set( "holdtype", 4 ); // Set holdtype to grab");

		// Raycast
		var start = Scene.Camera.WorldPosition;
		var dir = Scene.Camera.WorldTransform.Forward;
		var ray = new Ray( start, dir );
		var tr = Scene.Trace.Ray( ray, GrabRange )
			.Radius( 20f )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();

		if ( !tr.Hit )
			return null;

		var targetBody = tr.GameObject.Components.Get<Rigidbody>();
		if ( targetBody == null )
			return null;

		return targetBody;
	}

	private void ReleaseGrab()
	{
		// Animation
		TargetRenderer.Set( "holdtype", 0 ); // Set holdtype to grab");

		if ( _grabbedBody == null )
			return;

		_grabbedBody.AngularDamping = 0f;
		_grabbedBody.LinearDamping = 0f;
		_grabbedBody = null;
	}

	private void ApplyGrabForce()
	{
		if ( _grabbedBody == null )
		{
			ReleaseGrab();
			return;
		}

		var grabbedBodyPosition = _grabbedBody.WorldPosition;
		var holdPosition = TargetController.EyePosition + (TargetController.EyeAngles.Forward * 50f);

		var pullDirection = (holdPosition - grabbedBodyPosition).Normal;
		var distance = grabbedBodyPosition.Distance( holdPosition );
		var maxDistance = 50f;
		var forceMultiplier = MathX.Clamp( distance / maxDistance, 0f, 1f );

		_grabbedBody.AngularDamping = 5f;
		_grabbedBody.LinearDamping = 5f;
		_grabbedBody.ApplyForce( pullDirection * 3000f * _grabbedBody.Mass * forceMultiplier );
	}
}