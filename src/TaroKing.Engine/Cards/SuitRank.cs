namespace TaroKing.Engine.Cards;

/// <summary>
/// Rank inside a plain suit, ordered by trick-taking power (1 = weakest, 8 = king).
/// The four pip ranks print differently per colour: black runs 7, 8, 9, 10 upwards,
/// red runs 4, 3, 2, 1 upwards, so <see cref="Pip1"/> is the 7 in clubs and the 4 in hearts.
/// </summary>
public enum SuitRank {
	Pip1 = 1,
	Pip2 = 2,
	Pip3 = 3,
	Pip4 = 4,

	/// <summary>Fant.</summary>
	Jack = 5,

	/// <summary>Kavalir.</summary>
	Knight = 6,

	/// <summary>Dama.</summary>
	Queen = 7,

	/// <summary>Kralj — 5 points, and the card that gets called in partnership contracts.</summary>
	King = 8
}
