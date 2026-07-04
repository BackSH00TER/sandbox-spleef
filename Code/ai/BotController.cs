using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

/// <summary>
/// Controls a bot: picks a target tile, walks toward it, and jumps or combo
/// jump+leaps when a gap is in the way.
/// </summary>
public sealed class BotController : Component
{
	[Sync] public string BotName { get; set; } = "Bot";

	// Stable 0..N-1 index used to key per-bot names, outfits, and leaderboard entries
	// so identity survives lobby ↔ game scene swaps.
	[Sync] public int Slot { get; set; }

	private const float MoveSpeed = 200f;
	// XY distance to a target tile's center at which the bot considers itself
	// arrived and picks the next tile.
	private const float ArrivalRadius = 40f;
	private const float JumpUpVelocity = 300f;

	// Max XY distance the bot will attempt to cover via each launch tier.
	private const float WalkReach = 180f;
	private const float JumpReach = 140f;
	private const float DiveReach = 350f;
	private const float JumpLeapReach = 440f;

	// Delay between the initial jump and the follow-up leap in a combo. Short enough
	// that the bot is still rising so the leap's forward velocity stacks on the jump.
	private const float ComboLeapDelay = 0.08f;

	private PlayerController _controller;
	private PlayerLeap _leap;
	private Vector3? _targetPosition;
	private Tile _targetTile;
	private Tile _previousTile;
	private Vector3 _lastHeading;
	private RealTimeSince _sinceComboJump;
	private bool _isComboLeapPending;

	/// <summary>Host-only. Configure a freshly spawned bot.</summary>
	public void Initialize( string name, int slot )
	{
		BotName = name;
		Slot = slot;

		PlayerReadyState state = GetComponent<PlayerReadyState>();
		if ( state.IsValid() ) state.IsReady = true;
	}

	protected override void OnStart()
	{
		_controller = GetComponent<PlayerController>();
		_leap = GetComponent<PlayerLeap>();
		SuppressHostInput();
	}

	protected override void OnFixedUpdate()
	{
		// Only the host makes decisions for the bot.
		if ( !Network.IsOwner ) return;
		if ( !_controller.IsValid() ) return;

		// Re-assert every tick: PlayerLeap.FinishLeap or a network sync can flip
		// these back on and let the host's live input drive the bot's controller.
		SuppressHostInput();

		GameManager gameManager = GameManager.Current;
		if ( gameManager == null || !gameManager.GameInProgress )
		{
			// Stand idle while in the lobby
			Idle();
			return;
		}

		if ( _leap.IsValid() && _leap.IsLeaping ) return;

		// If a jump+leap combo is pending, wait for the delay to elapse before firing the leap.
		if ( _isComboLeapPending )
		{
			if ( _sinceComboJump >= ComboLeapDelay )
			{
				_isComboLeapPending = false;
				if ( IsLeapReady() ) _leap.BeginLeap();
			}
			return;
		}

		if ( ShouldRetarget() ) PickTarget();

		if ( _targetPosition.HasValue )
		{
			LaunchIfEdgeAhead();
			MoveTowardTarget( _targetPosition.Value );
		}
		else
		{
			_controller.WishVelocity = Vector3.Zero;
		}
	}

	// The host owns bot GameObjects, so without this the host's own keyboard/mouse
	// would drive every bot's PlayerController alongside their own character.
	private void SuppressHostInput()
	{
		if ( !_controller.IsValid() ) return;
		_controller.UseInputControls = false;
		_controller.UseCameraControls = false;
		_controller.UseLookControls = false;
	}

	// Stand idle in place, not moving toward any target.
	private void Idle()
	{
		_controller.WishVelocity = Vector3.Zero;
		_targetPosition = null;
	}

	private bool IsLeapReady()
	{
		return _leap.IsValid() && !_leap.IsLeaping && _leap.LeapCooldownTime <= 0f;
	}

	// True when the bot should pick a new tile to move toward: no current target,
	// the current target has crumbled / become unsafe, or we've reached it.
	private bool ShouldRetarget()
	{
		if ( !_targetPosition.HasValue ) return true;
		// If the target tile is gone or unsafe, retarget.
		if ( _targetTile != null && (!_targetTile.IsValid() || !_targetTile.IsSafe) ) return true;

		float distXY = (WorldPosition.WithZ( 0 ) - _targetPosition.Value.WithZ( 0 )).Length;
		return distXY < ArrivalRadius;
	}

	// Pick a new target tile and set _targetPosition to its center.
	// If no safe tiles exist, _targetPosition is set to null and the bot will idle.
	private void PickTarget()
	{
		// Track the tile we're leaving so we don't turn around onto one that may have already crumbled behind us.
		_previousTile = _targetTile;

		Vector3 myPos = WorldPosition;

		// Creates the how high/low to look in the z direction
		// barely up (bots can't climb), well below so tiles on
		// the next layer show up as soon as we leave this one, lets us air-steer
		// toward a landing spot instead of free-falling.
		float layerSpacing = TileManager.Current.IsValid() ? TileManager.Current.LayerSpacing : 400f;
		float maxZAbove = 20f;
		float maxZBelow = layerSpacing * 1.5f;

		// Gather all safe tiles within a reasonable range, sorted by XY distance.
		List<(Tile tile, float distXY)> candidates = Scene.GetAllComponents<Tile>()
			.Where( t => t.IsValid() && t.IsSafe && t != _previousTile
				&& t.WorldPosition.z - myPos.z <= maxZAbove
				&& myPos.z - t.WorldPosition.z <= maxZBelow )
			.Select( t => (tile: t, distXY: (t.WorldPosition - myPos).WithZ( 0 ).Length) )
			.Where( x => x.distXY > 8f ) // Filter out the tile we're already on
			.OrderBy( x => x.distXY )
			.ToList();

		if ( candidates.Count == 0 )
		{
			_targetPosition = null;
			return;
		}

		// Only truly walkable candidates get the walk treatment. Without the ground
		// trace, a tile 160 units away across a gap counts as walkable and the bot
		// walks straight off the edge.
		List<(Tile tile, float distXY)> walkableTiles = candidates
			.Where( x => x.distXY <= WalkReach && HasGroundBetween( myPos, x.tile.WorldPosition ) )
			.ToList();

		if ( walkableTiles.Count > 0 )
		{
			SetTargetTile( PickTileWeightedByHeading( walkableTiles, myPos ).tile );
			return;
		}

		// No safe walk exists. Commit to the nearest gap-crossing tile and jump
		// from here. Makes sure the launch happens from the current tile before bot steps off the edge.
		(Tile tile, float distXY) nearest = candidates[0];
		SetTargetTile( nearest.tile );
		FaceTarget( nearest.tile.WorldPosition );
		LaunchAt( nearest.distXY );
	}

	private void SetTargetTile( Tile tile )
	{
		_targetTile = tile;
		_targetPosition = tile.WorldPosition;
	}

	// Fire the cheapest launch (normal jump or dive) that covers the given distance. If nothing
	// fits, still fire something, better to try and fall short than walk off with
	// no attempt at all.
	private void LaunchAt( float distanceXY )
	{
		// No mid-air launches. Once committed to a trajectory, the bot has to ride
		// it out. Prevents chain-jumping / infinite hang time when PickTarget or
		// LaunchIfEdgeAhead re-triggers mid-fall.
		if ( !_controller.IsOnGround ) return;

		bool leapReady = IsLeapReady();

		if ( distanceXY <= JumpReach )
		{
			_controller.Jump( Vector3.Up * JumpUpVelocity );
		}
		else if ( distanceXY <= DiveReach && leapReady )
		{
			_leap.BeginLeap();
		}
		else if ( leapReady )
		{
			_controller.Jump( Vector3.Up * JumpUpVelocity );
			_isComboLeapPending = true;
			_sinceComboJump = 0f;
		}
		else
		{
			_controller.Jump( Vector3.Up * JumpUpVelocity );
		}
	}

	// Trace a short distance ahead; if there's no ground there, launch now instead
	// of walking off. Backs up PickTarget's ground check for narrow gaps or tiles
	// that crumbled mid-walk. Helps narrow down cases where bot just walks off edge and tries to jump too late.
	private void LaunchIfEdgeAhead()
	{
		if ( !_controller.IsOnGround ) return;
		if ( _isComboLeapPending ) return;
		if ( _lastHeading.LengthSquared < 0.01f ) return;

		Vector3 ahead = WorldPosition + _lastHeading * 60f;
		SceneTraceResult trace = Scene.Trace
			.Ray( ahead + Vector3.Up * 20f, ahead + Vector3.Down * 80f )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();
		if ( trace.Hit ) return;

		FaceTarget( _targetPosition.Value );
		float distToTarget = (_targetPosition.Value.WithZ( 0 ) - WorldPosition.WithZ( 0 )).Length;
		LaunchAt( distToTarget );
	}

	// Sample a few midpoints along the path; if any lacks ground within a
	// reasonable step distance, the path has a gap and isn't walkable.
	private bool HasGroundBetween( Vector3 from, Vector3 to )
	{
		const int Samples = 3;
		for ( int i = 1; i <= Samples; i++ )
		{
			float t = i / (float)(Samples + 1);
			Vector3 sample = Vector3.Lerp( from, to, t );
			SceneTraceResult trace = Scene.Trace
				.Ray( sample + Vector3.Up * 20f, sample + Vector3.Down * 200f )
				.IgnoreGameObjectHierarchy( GameObject )
				.Run();
			if ( !trace.Hit ) return false;
		}
		return true;
	}

	// Random pick biased toward the current heading so bots don't zigzag between
	// adjacent tiles every time they arrive somewhere.
	private (Tile tile, float distXY) PickTileWeightedByHeading(
		List<(Tile tile, float distXY)> walkableTiles, Vector3 myPos )
	{
		if ( walkableTiles.Count == 1 ) return walkableTiles[0];
		if ( _lastHeading.LengthSquared < 0.01f )
			return walkableTiles[Game.Random.Int( walkableTiles.Count - 1 )];

		// Weight each tile by how aligned its direction is with the current heading.
		// dot = 1 (straight ahead), 0 (perpendicular), -1 (behind). Floor at 0.15 so
		// behind-us tiles are unlikely but not impossible.
		float total = 0f;
		float[] weights = new float[walkableTiles.Count];
		for ( int i = 0; i < walkableTiles.Count; i++ )
		{
			Vector3 toTile = (walkableTiles[i].tile.WorldPosition - myPos).WithZ( 0 );
			float weight = 0.1f;
			if ( toTile.LengthSquared >= 0.01f )
			{
				float dot = Vector3.Dot( toTile.Normal, _lastHeading );
				weight = MathF.Max( 0.15f, 1f + dot );
			}
			weights[i] = weight;
			total += weight;
		}

		// Pick a random point in the total weight; the tile whose slice contains that point wins.
		float roll = Game.Random.Float( 0f, total );
		float acc = 0f;
		for ( int i = 0; i < walkableTiles.Count; i++ )
		{
			acc += weights[i];
			if ( roll <= acc ) return walkableTiles[i];
		}
		return walkableTiles[walkableTiles.Count - 1];
	}

	private void MoveTowardTarget( Vector3 target )
	{
		Vector3 toTarget = (target - WorldPosition).WithZ( 0 );
		if ( toTarget.LengthSquared < 0.01f )
		{
			_controller.WishVelocity = Vector3.Zero;
			return;
		}

		Vector3 dir = toTarget.Normal;
		_lastHeading = dir;
		_controller.WishVelocity = dir * MoveSpeed;
		FaceTarget( target );
	}

	private void FaceTarget( Vector3 target )
	{
		Vector3 flat = (target - WorldPosition).WithZ( 0 );
		if ( flat.LengthSquared < 0.01f ) return;
		_controller.EyeAngles = Rotation.LookAt( flat.Normal, Vector3.Up ).Angles();
	}
}

