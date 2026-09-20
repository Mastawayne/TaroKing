using System.Globalization;

using TaroKing.Bots;

namespace TaroKing.App.Services;

/// <summary>When a session is finished.</summary>
public enum SessionMode {
	/// <summary>A fixed number of hands.</summary>
	Hands = 0,

	/// <summary>Until somebody reaches the target score.</summary>
	Target = 1,

	/// <summary>Never, until the player walks away.</summary>
	Endless = 2
}

/// <summary>How the three bots at the table behave.</summary>
public enum TableStyle {
	/// <summary>One of each, which plays like a real table.</summary>
	Mixed = 0,

	/// <summary>All three bid only on hands they are sure of.</summary>
	Cautious = 1,

	/// <summary>All three bid thin and announce on speculation.</summary>
	Bold = 2
}

/// <summary>Everything the player chooses before the first card is dealt.</summary>
public sealed record SessionOptions {

	public SessionMode Mode { get; init; } = SessionMode.Hands;

	/// <summary>Hands to play in <see cref="SessionMode.Hands"/>.</summary>
	public int HandCount { get; init; } = 8;

	/// <summary>The score that ends a <see cref="SessionMode.Target"/> session.</summary>
	public int TargetScore { get; init; } = 500;

	public TableStyle Style { get; init; } = TableStyle.Mixed;

	/// <summary>How long a bot pretends to think, in milliseconds.</summary>
	public int BotDelayMs { get; init; } = 650;

	/// <summary>Fix the seed to replay an entire session card for card. Null means a fresh shuffle.</summary>
	public int? Seed { get; init; }

	/// <summary>
	/// The first hand was a compulsory klop after a thrown-out deal. Only a replay of a stored hand
	/// sets this; a fresh session works it out from the cards.
	/// </summary>
	public bool ReplayCompulsoryKlop { get; init; }

	/// <summary>Which chair the human takes. Forehand by default; a replay puts you where you sat.</summary>
	public int HumanSeat { get; init; }

	/// <summary>A hand played again from the history. Played for the fun of it; never written down.</summary>
	public bool IsReplay { get; init; }

	/// <summary>Let a bot take the human's seat too. Used by the tests and by a table left to run itself.</summary>
	public bool AutoPlayHuman { get; init; }

	public BotProfile ProfileFor(int seat) => Style switch {
		TableStyle.Cautious => BotProfile.Cautious,
		TableStyle.Bold => BotProfile.Aggressive,
		_ => seat switch {
			1 => BotProfile.Normal,
			2 => BotProfile.Cautious,
			_ => BotProfile.Aggressive
		}
	};

	public string Describe() => Mode switch {
		SessionMode.Hands => $"{HandCount} partij",
		SessionMode.Target => $"do {TargetScore.ToString(CultureInfo.InvariantCulture)} točk",
		_ => "brez konca"
	};

	public string StyleName() => Style switch {
		TableStyle.Cautious => "previdna miza",
		TableStyle.Bold => "drzna miza",
		_ => "mešana miza"
	};
}
