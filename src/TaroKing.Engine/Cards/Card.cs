namespace TaroKing.Engine.Cards;

/// <summary>
/// One of the 54 cards. <see cref="Rank"/> is always the trick-taking power inside its own
/// <see cref="Suit"/>: 1-8 for a plain suit (8 = king), 1-22 for trumps (1 = pagat, 21 = mond, 22 = škis).
/// Comparing two cards from different suits says nothing about who wins a trick — that is the trick's job.
/// </summary>
public readonly record struct Card : IComparable<Card> {

	private static readonly string[] RomanNumerals = [
		"I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI",
		"XII", "XIII", "XIV", "XV", "XVI", "XVII", "XVIII", "XIX", "XX", "XXI"
	];

	public Card(Suit suit, int rank) {
		if (!Enum.IsDefined(suit)) {
			throw new ArgumentOutOfRangeException(nameof(suit), suit, "Unknown suit.");
		}

		int maxRank = suit == Suit.Trump ? TarokConstants.TrumpCount : TarokConstants.SuitSize;
		if (rank < 1 || rank > maxRank) {
			throw new ArgumentOutOfRangeException(nameof(rank), rank, $"Rank must be between 1 and {maxRank} for {suit}.");
		}

		Suit = suit;
		Rank = rank;
	}

	public Suit Suit { get; }

	public int Rank { get; }

	// --- well-known cards ---

	/// <summary>Pagat (I) — the lowest trump, worth 5 points.</summary>
	public static Card Pagat => new(Suit.Trump, 1);

	/// <summary>Mond (XXI) — worth 5 points, and lost to the škis costs 20.</summary>
	public static Card Mond => new(Suit.Trump, 21);

	/// <summary>Škis — the highest trump, worth 5 points.</summary>
	public static Card Skis => new(Suit.Trump, TarokConstants.TrumpCount);

	// --- factories ---

	/// <summary>A numbered trump, I through XXI. Use <see cref="Skis"/> for the škis.</summary>
	public static Card Trump(int numeral) {
		if (numeral < 1 || numeral > 21) {
			throw new ArgumentOutOfRangeException(nameof(numeral), numeral, "Trump numerals run from I to XXI.");
		}

		return new Card(Suit.Trump, numeral);
	}

	/// <summary>A plain-suit card.</summary>
	public static Card Of(Suit suit, SuitRank rank) {
		if (suit == Suit.Trump) {
			throw new ArgumentOutOfRangeException(nameof(suit), suit, "Use Trump() or Skis for trumps.");
		}

		if (!Enum.IsDefined(rank)) {
			throw new ArgumentOutOfRangeException(nameof(rank), rank, "Unknown suit rank.");
		}

		return new Card(suit, (int)rank);
	}

	// --- classification ---

	public bool IsTrump => Suit == Suit.Trump;

	public bool IsSkis => Suit == Suit.Trump && Rank == TarokConstants.TrumpCount;

	public bool IsMond => Suit == Suit.Trump && Rank == 21;

	public bool IsPagat => Suit == Suit.Trump && Rank == 1;

	public bool IsKing => Suit != Suit.Trump && Rank == (int)SuitRank.King;

	/// <summary>Škis, mond and pagat — the three trumps that make up the trula.</summary>
	public bool IsTrulaCard => IsSkis || IsMond || IsPagat;

	/// <summary>A card that may never be discarded to the talon: kings, škis, mond and pagat.</summary>
	public bool IsFivePointer => Points == 5;

	/// <summary>The rank as a plain-suit rank. Throws for trumps.</summary>
	public SuitRank AsSuitRank => Suit != Suit.Trump
		? (SuitRank)Rank
		: throw new InvalidOperationException("Trumps have no suit rank.");

	/// <summary>Card value: king / škis / mond / pagat 5, queen 4, knight 3, jack 2, everything else 1.</summary>
	public int Points {
		get {
			if (Suit == Suit.Trump) {
				return IsTrulaCard ? 5 : 1;
			}

			return Rank switch {
				(int)SuitRank.King => 5,
				(int)SuitRank.Queen => 4,
				(int)SuitRank.Knight => 3,
				(int)SuitRank.Jack => 2,
				_ => 1
			};
		}
	}

	// --- display ---

	/// <summary>"XXI", "Škis", "K♥", "7♣".</summary>
	public string ShortName {
		get {
			if (Suit == Suit.Trump) {
				return IsSkis ? "Škis" : RomanNumerals[Rank - 1];
			}

			return RankSymbol() + Suit.Symbol();
		}
	}

	private string RankSymbol() => ((SuitRank)Rank) switch {
		SuitRank.King => "K",
		SuitRank.Queen => "Q",
		SuitRank.Knight => "C",
		SuitRank.Jack => "J",
		_ => PipLabel()
	};

	/// <summary>Black pips read 7, 8, 9, 10 upwards; red pips read 4, 3, 2, 1 upwards.</summary>
	private string PipLabel() {
		int pip = Rank; // 1..4
		return Suit.IsBlack()
			? (6 + pip).ToString(System.Globalization.CultureInfo.InvariantCulture)
			: (5 - pip).ToString(System.Globalization.CultureInfo.InvariantCulture);
	}

	public override string ToString() => ShortName;

	/// <summary>Display ordering: suit first, then rank. Not a trick-winning comparison.</summary>
	public int CompareTo(Card other) {
		int bySuit = Suit.CompareTo(other.Suit);
		return bySuit != 0 ? bySuit : Rank.CompareTo(other.Rank);
	}

	public static bool operator <(Card left, Card right) => left.CompareTo(right) < 0;

	public static bool operator <=(Card left, Card right) => left.CompareTo(right) <= 0;

	public static bool operator >(Card left, Card right) => left.CompareTo(right) > 0;

	public static bool operator >=(Card left, Card right) => left.CompareTo(right) >= 0;
}
