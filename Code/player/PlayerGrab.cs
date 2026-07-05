using Sandbox;
using Sandbox.Physics;

public sealed class PlayerGrab : Component
{
	[Property, InputAction] public string GrabInput { get; set; } = "use";
	[Property] public float GrabRange { get; set; } = 150f;

	private PhysicsJoint _grabJoint;

	protected override void OnUpdate()
	{
		if ( Input.Pressed( GrabInput ) )
		{
			TryGrab();
		}

		if ( Input.Released( GrabInput ) && _grabJoint != null )
		{
			_grabJoint.Remove();
			_grabJoint = null;
		}
	}

	private void TryGrab()
	{
		var start = Scene.Camera.WorldPosition;
		var dir = Scene.Camera.WorldTransform.Forward;
		var ray = new Ray( start, dir );

		var tr = Scene.Trace.Ray( ray, GrabRange )
			.Run();

		if ( !tr.Hit ) return;

		var target = tr.GameObject.Components.Get<PlayerController>();
		if ( target == null ) return;

		var myBody = GameObject.Components.Get<Rigidbody>();
		var targetBody = tr.GameObject.Components.Get<Rigidbody>();

		if ( myBody == null || targetBody == null ) return;

		_grabJoint?.Remove();

		_grabJoint = PhysicsJoint.CreateFixed(
			new PhysicsPoint( myBody.PhysicsBody, Vector3.Zero ),
			new PhysicsPoint( targetBody.PhysicsBody, targetBody.WorldTransform.PointToLocal( tr.HitPosition ) )
		);
	}
}