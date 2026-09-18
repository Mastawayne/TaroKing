namespace TaroKing.Engine;

/// <summary>Fixed numbers of the four-player Slovenian tarok game.</summary>
public static class TarokConstants {

	/// <summary>Players at a full table.</summary>
	public const int PlayerCount = 4;

	/// <summary>Cards in the pack: 22 trumps + 4 suits of 8.</summary>
	public const int DeckSize = 54;

	/// <summary>Trumps, from Pagat (I) to Mond (XXI) plus the Škis.</summary>
	public const int TrumpCount = 22;

	/// <summary>Cards per suit.</summary>
	public const int SuitSize = 8;

	/// <summary>Cards dealt face down to the talon.</summary>
	public const int TalonSize = 6;

	/// <summary>Cards each player holds after the deal.</summary>
	public const int HandSize = 12;

	/// <summary>Tricks played in one hand.</summary>
	public const int TrickCount = 12;

	/// <summary>Total card points in the pack.</summary>
	public const int TotalCardPoints = 70;

	/// <summary>Card points the declaring side needs to win.</summary>
	public const int WinningCardPoints = 36;

	/// <summary>Baseline the difference is measured from.</summary>
	public const int DifferenceBaseline = 35;

	/// <summary>Personal penalty for letting the Mond be captured by the Škis.</summary>
	public const int CapturedMondPenalty = -20;

	/// <summary>Penalty per uncancelled radlc at the end of a session.</summary>
	public const int UncancelledRadlcPenalty = -100;
}
