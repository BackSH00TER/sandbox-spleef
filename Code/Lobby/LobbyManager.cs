using System;
using System.Linq;

/// <summary>
/// Lives in the lobby scene. Host watches every <see cref="PlayerReadyState"/>
/// in the scene; once all connected players are ready (and we have at least
/// <see cref="MinPlayers"/>), it runs a short launch countdown and then loads
/// the gameplay scene for everyone. Walking out of the ready zone before the
/// countdown ends cancels the launch.
/// </summary>
public sealed class LobbyManager : Component
{
	/// <summary>Gameplay scene to load once everyone is ready.</summary>
	[Property] public SceneFile GameScene { get; set; }

	/// <summary>Minimum number of connected players required before the launch countdown can start.</summary>
	[Property] public int MinPlayers { get; set; } = 1;

	/// <summary>True on every client while the launch countdown is running.</summary>
	public bool IsLaunching { get; private set; }

	/// <summary>Seconds left on the launch countdown on this client. Only meaningful while <see cref="IsLaunching"/>.</summary>
	public float LaunchSecondsRemaining => MathF.Max( 0f, (float)_launchAt );

	/// <summary>Scene-wide singleton so the lobby UI can find us without a hard reference.</summary>
	public static LobbyManager Current { get; private set; }

	/// <summary>
	/// Cross-scene banner text (e.g. "host left"). Set from another scene right before
	/// loading the lobby; the lobby UI shows it until <see cref="LobbyMessageExpires"/> elapses.
	/// </summary>
	public static string LobbyMessage { get; private set; }

	/// <summary>Real-time deadline for <see cref="LobbyMessage"/>. Static so it survives the scene swap.</summary>
	public static RealTimeUntil LobbyMessageExpires { get; private set; }

	private const float LobbyMessageDuration = 8f;

	/// <summary>Show a banner in the lobby UI for the next <see cref="LobbyMessageDuration"/> seconds.</summary>
	public static void ShowLobbyMessage( string message )
	{
		LobbyMessage = message;
		LobbyMessageExpires = LobbyMessageDuration;
	}

	/// <summary>True while <see cref="LobbyMessage"/> should be rendered.</summary>
	public static bool HasActiveLobbyMessage => !string.IsNullOrEmpty( LobbyMessage ) && LobbyMessageExpires > 0f;

	/// <summary>Seconds to wait, with everyone still ready, before actually loading the game scene.</summary>
	private float LaunchSeconds { get; set; } = 3f;

	/// <summary>Seconds the screen takes to fade to black at the tail of the launch countdown. Must be &lt;= <see cref="LaunchSeconds"/>.</summary>
	private const float LaunchFadeDuration = 2f;

	private TimeUntil _launchAt;
	private bool _hasLaunched;
	private bool _hasTriggeredLaunchFade;

	private float percentReadyRequired = 0.5f; // 50% of players

	protected override void OnEnabled()
	{
		Current = this;
	}

	protected override void OnDisabled()
	{
		if ( Current == this )
			Current = null;
	}

	protected override void OnUpdate()
	{
		// Per-client: kick off the screen fade-out once the countdown enters the fade window.
		if ( IsLaunching && !_hasTriggeredLaunchFade && (float)_launchAt <= LaunchFadeDuration )
		{
			_hasTriggeredLaunchFade = true;
			ScreenFade.FadeOut( LaunchFadeDuration );
		}

		// Host owns the state machine; everyone runs the launch-elapsed check locally
		// (broadcast-started, so all clients hit zero at roughly the same time).
		if ( IsLaunching && (float)_launchAt <= 0f )
		{
			if ( Networking.IsHost && !_hasLaunched )
			{
				_hasLaunched = true;
				LoadGameScene();
			}
			IsLaunching = false;
		}

		if ( !Networking.IsHost ) return;
		if ( _hasLaunched ) return;

		var allReadyStates = Scene.GetAllComponents<PlayerReadyState>().ToList();
		// Bots don't count — they're always ready. Requiring the real player(s) to
		// ready-up is what actually gates the launch.
		var realPlayerStates = allReadyStates.Where( s => s.GetComponent<BotBrain>() == null ).ToList();
		int readyCount = realPlayerStates.Count( s => s.IsReady );
		bool hasMajority = realPlayerStates.Count >= MinPlayers
			&& readyCount >= realPlayerStates.Count * percentReadyRequired;

		if ( hasMajority && !IsLaunching )
		{
			BroadcastCountdownStart( LaunchSeconds );
		}
		else if ( !hasMajority && IsLaunching )
		{
			BroadcastCountdownCancel();
		}
	}

	[Rpc.Broadcast]
	private void BroadcastCountdownStart( float seconds )
	{
		_launchAt = seconds;
		IsLaunching = true;
		_hasTriggeredLaunchFade = false;
	}

	[Rpc.Broadcast]
	private void BroadcastCountdownCancel()
	{
		IsLaunching = false;
		if ( _hasTriggeredLaunchFade )
		{
			_hasTriggeredLaunchFade = false;
			ScreenFade.FadeIn( LaunchFadeDuration );
		}
	}

	private void LoadGameScene()
	{
		if ( GameScene is null )
		{
			Log.Warning( "LobbyManager: GameScene is not assigned, can't launch." );
			return;
		}

		BroadcastLoadGameScene();
	}

	[Rpc.Broadcast]
	private void BroadcastLoadGameScene()
	{
		Game.ActiveScene.LoadFromFile( GameScene.ResourcePath );
	}
}
