using System;

/// <summary>
/// Random display name pool for AI bots. Names are chosen at spawn and
/// stored on the bot's <see cref="BotBrain"/>.
/// </summary>
public static class BotNames
{
	private static readonly string[] _names = new[]
	{
		"Waffle", "Pixel", "Turbo", "Biscuit", "Zap",
		"Noodle", "Bumper", "Sprocket", "Wobble", "Fizz",
		"Muffin", "Rocket", "Pickle", "Doodle", "Blip",
		"Gadget", "Crumb", "Squish", "Boop", "Tumble",
	};

	public static string Random( Random rng )
	{
		return _names[rng.Next( _names.Length )];
	}

	public static string Random()
	{
		return _names[Sandbox.Game.Random.Int( _names.Length - 1 )];
	}
}
