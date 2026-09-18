namespace TaroKing.Engine.Cards;

/// <summary>The five card groups: four plain suits and the trumps.</summary>
public enum Suit {
	/// <summary>Križ ♣ — black suit.</summary>
	Clubs = 0,

	/// <summary>Pik ♠ — black suit.</summary>
	Spades = 1,

	/// <summary>Srce ♥ — red suit.</summary>
	Hearts = 2,

	/// <summary>Karo ♦ — red suit.</summary>
	Diamonds = 3,

	/// <summary>Tarok — the 22 trumps.</summary>
	Trump = 4
}

public static class SuitExtensions {

	/// <summary>Clubs and spades, where the pip cards run 7-8-9-10 upwards.</summary>
	public static bool IsBlack(this Suit suit) => suit is Suit.Clubs or Suit.Spades;

	/// <summary>Hearts and diamonds, where the pip cards run 4-3-2-1 upwards.</summary>
	public static bool IsRed(this Suit suit) => suit is Suit.Hearts or Suit.Diamonds;

	/// <summary>Everything except <see cref="Suit.Trump"/>.</summary>
	public static bool IsPlainSuit(this Suit suit) => suit != Suit.Trump;

	public static string Symbol(this Suit suit) => suit switch {
		Suit.Clubs => "♣",
		Suit.Spades => "♠",
		Suit.Hearts => "♥",
		Suit.Diamonds => "♦",
		Suit.Trump => "T",
		_ => throw new ArgumentOutOfRangeException(nameof(suit), suit, "Unknown suit.")
	};

	/// <summary>Slovenian name, as used at the table.</summary>
	public static string SlovenianName(this Suit suit) => suit switch {
		Suit.Clubs => "križ",
		Suit.Spades => "pik",
		Suit.Hearts => "srce",
		Suit.Diamonds => "karo",
		Suit.Trump => "tarok",
		_ => throw new ArgumentOutOfRangeException(nameof(suit), suit, "Unknown suit.")
	};
}
