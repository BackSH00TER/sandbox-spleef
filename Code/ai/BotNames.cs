using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Persistent per-slot name assignment for AI bots. The first name a bot is
/// given for slot N is cached statically, so the same slot keeps its name across
/// lobby ↔ game scene transitions (bots are respawned each transition and would
/// otherwise re-roll their name). Reset on host restart.
/// </summary>
public static class BotNames
{
	private static readonly string[] _pool = new[]
	{
		"Waffle", "Pixel", "Turbo", "Biscuit", "Zap",
		"Noodle", "Bumper", "Sprocket", "Wobble", "Fizz",
		"Muffin", "Rocket", "Pickle", "Doodle", "Blip",
		"Gadget", "Crumb", "Squish", "Boop", "Tumble",
	};

	private static readonly List<string> _assigned = new();

	/// <summary>
	/// Returns the name for the given bot slot (0-based). First access to a slot
	/// picks a fresh name from the pool (avoiding duplicates while possible);
	/// subsequent accesses return the same name for the session.
	/// </summary>
	public static string ForSlot( int slot )
	{
		while ( _assigned.Count <= slot )
		{
			List<string> remaining = _pool.Except( _assigned ).ToList();
			string next = remaining.Count > 0
				? remaining[Sandbox.Game.Random.Int( remaining.Count - 1 )]
				: _pool[Sandbox.Game.Random.Int( _pool.Length - 1 )];
			_assigned.Add( next );
		}
		return _assigned[slot];
	}
}
