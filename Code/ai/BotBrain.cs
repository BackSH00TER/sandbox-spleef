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

	// Synthetic per-bot id used in place of Network.Owner.SteamId for leaderboard/crown
	// lookups. All bots are host-owned so their owner SteamId is the host's — using
	// that would credit the host with every bot's win and put the crown on every bot.
	[Sync] public ulong LeaderboardId { get; set; }

	/// <summary>How often the bot re-picks a target tile (also fires on arrival).</summary>
	[Property] public float RetargetInterval { get; set; } = 1.5f;

	/// <summary>Walk speed applied via WishVelocity while pursuing a target.</summary>
	[Property] public float MoveSpeed { get; set; } = 200f;

	/// <summary>Max XY distance to a tile that still counts as "walkable adjacent".</summary>
	[Property] public float WalkReach { get; set; } = 180f;

	/// <summary>Max XY distance to a tile the bot will attempt to reach via a jump.</summary>
	[Property] public float JumpReach { get; set; } = 280f;

	/// <summary>Max XY distance to a tile the bot will attempt to reach via a dive (leap).</summary>
	[Property] public float DiveReach { get; set; } = 550f;

	/// <summary>Distance from the target position at which the bot re-targets.</summary>
	[Property] public float ArrivalRadius { get; set; } = 40f;

	/// <summary>Upward velocity applied when the bot chooses to jump (matches player Jump).</summary>
	[Property] public float JumpUpVelocity { get; set; } = 300f;

	private PlayerController _controller;
	private PlayerLeap _leap;
	private Vector3? _targetPosition;
	private RealTimeSince _sinceRetarget;

	/// <summary>Host-only. Configure a freshly spawned bot.</summary>
	public void Initialize( string name )
	{
		BotName = name;
		LeaderboardId = ((ulong)(uint)Guid.NewGuid().GetHashCode()) | 0x1000_0000_0000_0000UL;

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

		bool hasArrived = _targetPosition.HasValue
			&& (WorldPosition.WithZ( 0 ) - _targetPosition.Value.WithZ( 0 )).Length < ArrivalRadius;

		if ( !_targetPosition.HasValue || _sinceRetarget > RetargetInterval || hasArrived )
		{
			PickTarget();
			_sinceRetarget = 0f;
		}

		if ( _targetPosition.HasValue )
		{
			Steer( _targetPosition.Value );
		}
		else
		{
			_controller.WishVelocity = Vector3.Zero;
		}
	}

	private void PickTarget()
	{
		Vector3 myPos = WorldPosition;

		// Rank all safe tiles by XY distance. Ignore anything far below/above the
		// bot so a bot on layer 0 doesn't try to target a tile on layer 3.
		List<(Tile tile, float distXY, float distZ)> candidates = Scene.GetAllComponents<Tile>()
			.Where( t => t.IsValid() && t.IsSafe )
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

		// Prefer a random tile within walk range so bots don't all bee-line to the
		// same closest tile.
		List<(Tile tile, float distXY, float distZ)> walkable = candidates.Where( x => x.distXY <= WalkReach ).ToList();
		if ( walkable.Count > 0 )
		{
			var pick = walkable[Sandbox.Game.Random.Int( walkable.Count - 1 )];
			_targetPosition = pick.tile.WorldPosition;
			return;
		}

		// Nothing adjacent — try to jump to the nearest reachable tile.
		var jumpPick = candidates.FirstOrDefault( x => x.distXY <= JumpReach );
		if ( jumpPick.tile.IsValid() )
		{
			_targetPosition = jumpPick.tile.WorldPosition;
			FaceTarget( _targetPosition.Value );
			_controller.Jump( Vector3.Up * JumpUpVelocity );
			return;
		}

		// Still nothing — dive.
		var divePick = candidates.FirstOrDefault( x => x.distXY <= DiveReach );
		if ( divePick.tile.IsValid() && _leap != null && !_leap.IsLeaping && _leap.LeapCooldownTime <= 0f )
		{
			_targetPosition = divePick.tile.WorldPosition;
			FaceTarget( _targetPosition.Value );
			_leap.BeginLeap();
			return;
		}

		// Nothing safely reachable — just walk toward the nearest safe tile and hope.
		_targetPosition = candidates[0].tile.WorldPosition;
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
