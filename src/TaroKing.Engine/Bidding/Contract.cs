namespace TaroKing.Engine.Bidding;

/// <summary>
/// The contracts, in bidding order from lowest to highest. The numeric values are the bidding rank,
/// not the score — <see cref="ContractInfo.GameValue"/> holds the score.
/// </summary>
public enum Contract {
	/// <summary>Klop — every player for themselves, nobody wants a game. Forehand only.</summary>
	Klop = 0,

	/// <summary>Tri — three talon cards, calls a king. Forehand only.</summary>
	Three = 1,

	/// <summary>Dva — two talon cards, calls a king. The lowest bid available to the other players.</summary>
	Two = 2,

	/// <summary>Ena — one talon card, calls a king.</summary>
	One = 3,

	/// <summary>Solo tri — three talon cards, declarer plays alone.</summary>
	SoloThree = 4,

	/// <summary>Solo dva — two talon cards, declarer plays alone.</summary>
	SoloTwo = 5,

	/// <summary>Solo ena — one talon card, declarer plays alone.</summary>
	SoloOne = 6,

	/// <summary>Berač — declarer takes no trick at all.</summary>
	Beggar = 7,

	/// <summary>Solo brez — no talon, declarer alone, needs 36+ card points.</summary>
	SoloWithout = 8,

	/// <summary>Odprti berač — beggar with the declarer's hand face up after the first trick.</summary>
	OpenBeggar = 9,

	/// <summary>Barvni valat — every trick, with trumps behaving as an ordinary suit.</summary>
	ColourValat = 10,

	/// <summary>Valat — every trick.</summary>
	Valat = 11
}
