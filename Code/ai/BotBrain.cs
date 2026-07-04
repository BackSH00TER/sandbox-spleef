using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

/// <summary>
/// Bot marker + brain. Presence identifies a bot (see <see cref="LobbyAIToggle"/>,
/// <see cref="LobbyNetworkSpawner"/>, elimination/win paths). On the host it also
/// drives movement: pick a nearby safe tile, walk toward it; jump or dive if the
/// only safe tile is out of walking reach.
/// </summary>
public sealed class BotBrain : Component
{
	[Sync] public string BotName { get; set; } = "Bot";

	// Stable slot index (0..BotCount-1) used to key persistent name / outfit / leaderboard
	// lookups (see BotNames.ForSlot, BotOutfits.ApplyForSlot, Leaderboard.BotId) so bot #N
	// keeps its identity across lobby ↔ game scene transitions.
	[Sync] public int Slot { get; set; }

	/// <summary>How often the bot re-picks a target tile (also fires on arrival).</summary>
	private const float RetargetInterval = 1.5f;

	/// <summary>Walk speed applied via WishVelocity while pursuing a target.</summary>
	private const float MoveSpeed = 200f;

	/// <summary>Max XY distance to a tile that still counts as "walkable adjacent".</summary>
	private const float WalkReach = 180f;

	/// <summary>Max XY distance to a tile the bot will attempt to reach via a jump.</summary>
	private const float JumpReach = 140f;

	/// <summary>Max XY distance to a tile the bot will attempt to reach via a leap alone.</summary>
	private const float DiveReach = 350f;

	/// <summary>Max XY distance to a tile the bot will attempt to reach via a jump followed by a mid-air leap.</summary>
	private const float JumpLeapReach = 440f;

	/// <summary>Distance from the target position at which the bot re-targets.</summary>
	private const float ArrivalRadius = 40f;

	/// <summary>Upward velocity applied when the bot chooses to jump (matches player Jump).</summary>
	private const float JumpUpVelocity = 300f;

	private PlayerController _controller;
	private PlayerLeap _leap;
	private Vector3? _targetPosition;
	private Tile _targetTile;
	private Tile _previousTile;
	private Vector3 _lastHeading;
	private RealTimeSince _sinceRetarget;
	private RealTimeSince _sinceComboJump;
	private bool _isComboLeapPending;
	// Delay between the initial jump and the follow-up leap. Short enough that the
	// bot is still rising, so the leap's forward velocity stacks on the jump for max range.
	private const float ComboLeapDelay = 0.08f;

	/// <summary>Host-only. Configure a freshly spawned bot.</summary>
	public void Initialize( string name, int slot )
	{
		BotName = name;
		Slot = slot;

		// Bots are always ready so they don't gate the launch countdown.
		PlayerReadyState state = GetComponent<PlayerReadyState>();
		if ( state.IsValid() )
		{
			state.IsReady = true;
		}
	}

	protected override void OnStart()
	{
		_controller = GetComponent<PlayerController>();
		_leap = GetComponent<PlayerLeap>();

		// NetworkSpawn may reset PlayerController flags to prefab defaults; re-suppress here
		// so the host's local input can never drive this bot's PlayerController.
		if ( _controller.IsValid() )
		{
			_controller.UseInputControls = false;
			_controller.UseCameraControls = false;
			_controller.UseLookControls = false;
		}
	}

	protected override void OnFixedUpdate()
	{
		// Only the owner (host) makes decisions for the bot.
		if ( !Network.IsOwner ) return;
		if ( !_controller.IsValid() ) return;

		// Hard invariant: bots never consume live input. Re-assert every tick in case
		// something (PlayerLeap.FinishLeap, network sync, etc.) flipped it back on.
		_controller.UseInputControls = false;
		_controller.UseCameraControls = false;
		_controller.UseLookControls = false;

		GameManager gm = GameManager.Current;
		bool inGame = gm != null && gm.GameInProgress;
		if ( !inGame )
		{
			_controller.WishVelocity = Vector3.Zero;
			_targetPosition = null;
			return;
		}

		// If mid-leap, don't fight it — the leap has its own velocity.
		if ( _leap != null && _leap.IsLeaping )
		{
			return;
		}

		// Fire the deferred half of a jump+leap combo once the delay has elapsed.
		if ( _isComboLeapPending && _sinceComboJump >= ComboLeapDelay )
		{
			_isComboLeapPending = false;
			if ( _leap != null && !_leap.IsLeaping && _leap.LeapCooldownTime <= 0f )
			{
				_leap.BeginLeap();
			}
			return;
		}

		// While a combo leap is queued, don't re-steer — WishVelocity would fight the airborne trajectory.
		if ( _isComboLeapPending )
		{
			return;
		}

		bool hasArrived = _targetPosition.HasValue
			&& (WorldPosition.WithZ( 0 ) - _targetPosition.Value.WithZ( 0 )).Length < ArrivalRadius;

		// Retarget on arrival, on first tick, or if the tile we were headed to
		// became unsafe under us. Skipping the timer-based retarget keeps the bot
		// from flip-flopping mid-walk, which is the main source of jittery motion.
		bool targetInvalid = _targetTile != null && (!_targetTile.IsValid() || !_targetTile.IsSafe);
		if ( !_targetPosition.HasValue || hasArrived || targetInvalid )
		{
			PickTarget();
			_sinceRetarget = 0f;
		}

		if ( _targetPosition.HasValue )
		{
			// Edge-detect safety net BEFORE steering: if we'd step into a gap this
			// tick, launch now while still on solid ground. PickTarget's ground-
			// continuity check is coarse (3 samples), so gaps can still slip through.
			TryPreemptiveLaunch();
			Steer( _targetPosition.Value );
		}
		else
		{
			_controller.WishVelocity = Vector3.Zero;
		}
	}

	// Traces forward-and-down along the current heading; if there's no ground within
	// stepping distance, we're about to walk off an edge. Fire the cheapest reach
	// action that covers the remaining distance to the current target. Only fires
	// while grounded and moving (Jump is a no-op mid-air anyway).
	private void TryPreemptiveLaunch()
	{
		if ( !_controller.IsOnGround ) return;
		if ( _isComboLeapPending ) return;
		if ( _lastHeading.LengthSquared < 0.01f ) return;

		Vector3 ahead = WorldPosition + _lastHeading * 60f;
		SceneTraceResult trace = Scene.Trace.Ray( ahead + Vector3.Up * 20f, ahead + Vector3.Down * 80f )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();
		if ( trace.Hit ) return;

		float distToTarget = (_targetPosition.Value.WithZ( 0 ) - WorldPosition.WithZ( 0 )).Length;
		bool isLeapReady = _leap != null && !_leap.IsLeaping && _leap.LeapCooldownTime <= 0f;

		FaceTarget( _targetPosition.Value );

		if ( distToTarget <= JumpReach )
		{
			_controller.Jump( Vector3.Up * JumpUpVelocity );
			return;
		}
		if ( distToTarget <= DiveReach && isLeapReady )
		{
			_leap.BeginLeap();
			return;
		}
		if ( distToTarget <= JumpLeapReach && isLeapReady )
		{
			_controller.Jump( Vector3.Up * JumpUpVelocity );
			_isComboLeapPending = true;
			_sinceComboJump = 0f;
			return;
		}

		// Nothing reachable but we're about to step off — fire the biggest option
		// available anyway. Better to try and fail than walk off with no attempt.
		if ( isLeapReady )
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

	private void PickTarget()
	{
		Vector3 myPos = WorldPosition;

		// Remember where we came from so we don't turn straight around and walk back
		// onto a tile that may have crumbled behind us.
		_previousTile = _targetTile;

		// Rank all safe tiles by XY distance. Ignore anything far below/above the
		// bot so a bot on layer 0 doesn't try to target a tile on layer 3.
		List<(Tile tile, float distXY, float distZ)> candidates = Scene.GetAllComponents<Tile>()
			.Where( t => t.IsValid() && t.IsSafe && t != _previousTile )
			.Select( t =>
			{
				Vector3 delta = t.WorldPosition - myPos;
				return (t, delta.WithZ( 0 ).Length, MathF.Abs( delta.z ));
			} )
			.Where( x => x.Item3 < 120f )
			.OrderBy( x => x.Item2 )
			.ToList();

		// Filter out the tile we're basically standing on (very close XY, small Z).
		candidates = candidates.Where( x => x.distXY > 8f ).ToList();
		if ( candidates.Count == 0 )
		{
			_targetPosition = null;
			return;
		}

		// Prefer a walkable neighbour (continuous ground between us and it), weighted
		// toward the current heading so bots don't zigzag between adjacent tiles.
		// The ground-continuity check is critical: without it, a tile 160 units away
		// across a gap counts as "walkable" and the bot walks straight off the edge
		// instead of jumping.
		List<(Tile tile, float distXY, float distZ)> walkable = candidates
			.Where( x => x.distXY <= WalkReach && HasGroundBetween( myPos, x.tile.WorldPosition ) )
			.ToList();
		if ( walkable.Count > 0 )
		{
			(Tile tile, float distXY, float distZ) pick = PickWeightedByHeading( walkable, myPos );
			_targetPosition = pick.tile.WorldPosition;
			_targetTile = pick.tile;
			return;
		}

		// No safely walkable neighbour — commit to the nearest safe tile and pick
		// the cheapest airborne action that can cover the gap. Firing the jump/leap
		// from PickTarget (rather than mid-walk) means launch happens from the
		// current tile before the bot steps off the edge.
		(Tile tile, float distXY, float distZ) nearest = candidates[0];
		_targetPosition = nearest.tile.WorldPosition;
		_targetTile = nearest.tile;
		FaceTarget( _targetPosition.Value );

		bool isLeapReady = _leap != null && !_leap.IsLeaping && _leap.LeapCooldownTime <= 0f;

		if ( nearest.distXY <= JumpReach )
		{
			_controller.Jump( Vector3.Up * JumpUpVelocity );
			return;
		}
		if ( nearest.distXY <= DiveReach && isLeapReady )
		{
			_leap.BeginLeap();
			return;
		}
		if ( nearest.distXY <= JumpLeapReach && isLeapReady )
		{
			_controller.Jump( Vector3.Up * JumpUpVelocity );
			_isComboLeapPending = true;
			_sinceComboJump = 0f;
			return;
		}

		// Out of every reach tier — walk toward it and hope (or die trying).
	}

	// Sample a few points between us and the target, downward-trace each: if any
	// sample has no ground within a reasonable drop distance, there's a gap on the
	// path and we can't just walk across.
	private bool HasGroundBetween( Vector3 from, Vector3 to )
	{
		const int Samples = 3;
		for ( int i = 1; i <= Samples; i++ )
		{
			float t = i / (float)(Samples + 1);
			Vector3 sample = Vector3.Lerp( from, to, t );
			SceneTraceResult trace = Scene.Trace.Ray( sample + Vector3.Up * 20f, sample + Vector3.Down * 200f )
				.IgnoreGameObjectHierarchy( GameObject )
				.Run();
			if ( !trace.Hit ) return false;
		}
		return true;
	}

	// Weighted random pick from walkable candidates. Weight = 1 + dot(headingDir, dirToTile),
	// clamped to a small floor so tiles behind still have a small chance. When no
	// heading is set (first pick), degenerates to uniform random.
	private (Tile tile, float distXY, float distZ) PickWeightedByHeading(
		List<(Tile tile, float distXY, float distZ)> walkable, Vector3 myPos )
	{
		if ( walkable.Count == 1 ) return walkable[0];

		if ( _lastHeading.LengthSquared < 0.01f )
		{
			return walkable[Sandbox.Game.Random.Int( walkable.Count - 1 )];
		}

		float totalWeight = 0f;
		float[] weights = new float[walkable.Count];
		for ( int i = 0; i < walkable.Count; i++ )
		{
			Vector3 toTile = (walkable[i].tile.WorldPosition - myPos).WithZ( 0 );
			if ( toTile.LengthSquared < 0.01f )
			{
				weights[i] = 0.1f;
			}
			else
			{
				float dot = Vector3.Dot( toTile.Normal, _lastHeading );
				weights[i] = MathF.Max( 0.15f, 1f + dot );
			}
			totalWeight += weights[i];
		}

		float roll = Sandbox.Game.Random.Float( 0f, totalWeight );
		float acc = 0f;
		for ( int i = 0; i < walkable.Count; i++ )
		{
			acc += weights[i];
			if ( roll <= acc ) return walkable[i];
		}
		return walkable[walkable.Count - 1];
	}

	private void Steer( Vector3 target )
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
