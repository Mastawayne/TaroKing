namespace Taroksi.Bots;

/// <summary>How daring a bot is when bidding and announcing.</summary>
public enum BotProfile {
	/// <summary>Bids only on clearly strong hands, rarely announces.</summary>
	Cautious = 0,

	/// <summary>Balanced default.</summary>
	Normal = 1,

	/// <summary>Bids thin, announces pagat ultimo on speculation.</summary>
	Aggressive = 2
}
