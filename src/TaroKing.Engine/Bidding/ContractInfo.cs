namespace TaroKing.Engine.Bidding;

/// <summary>Everything the rules fix about one contract.</summary>
/// <param name="Contract">The contract itself.</param>
/// <param name="SlovenianName">The name used at the table.</param>
/// <param name="GameValue">Base score for winning it (klop uses this value with either sign).</param>
/// <param name="TalonCards">Cards the declarer takes from the talon; 0 when the talon is not touched.</param>
/// <param name="CallsKing">The declarer calls a king and plays with a hidden partner.</param>
/// <param name="IsSolo">The declarer plays alone against the other three.</param>
/// <param name="IsKlop">Every player for themselves; there is no declaring side.</param>
/// <param name="TakesNoTricks">The declarer must lose every trick.</param>
/// <param name="TakesAllTricks">The declarer must win every trick.</param>
/// <param name="CountsCardPoints">The hand is decided by card points: 36 or more wins it.</param>
/// <param name="ScoresDifference">The margin over 35, rounded to the nearest 5, is added to the score.</param>
/// <param name="AllowsBonuses">Trula, kings, ultimos and valat can be announced and scored.</param>
/// <param name="UsesNegativePlayRules">Must beat the highest card on the table; the pagat is held back.</param>
/// <param name="TrumpsArePlainSuit">Colour valat: trumps stop being trumps.</param>
/// <param name="ForehandLeads">Forehand leads the first trick; otherwise the declarer does.</param>
/// <param name="ForehandOnly">Only forehand can play it, and only when everybody else passes.</param>
public sealed record ContractInfo(
	Contract Contract,
	string SlovenianName,
	int GameValue,
	int TalonCards,
	bool CallsKing,
	bool IsSolo,
	bool IsKlop,
	bool TakesNoTricks,
	bool TakesAllTricks,
	bool CountsCardPoints,
	bool ScoresDifference,
	bool AllowsBonuses,
	bool UsesNegativePlayRules,
	bool TrumpsArePlainSuit,
	bool ForehandLeads,
	bool ForehandOnly);

/// <summary>The contract table.</summary>
public static class Contracts {

	private static readonly ContractInfo[] Table = [
		new(Contract.Klop, "klop", GameValue: 70, TalonCards: 0,
			CallsKing: false, IsSolo: false, IsKlop: true, TakesNoTricks: true, TakesAllTricks: false,
			CountsCardPoints: false, ScoresDifference: false, AllowsBonuses: false, UsesNegativePlayRules: true,
			TrumpsArePlainSuit: false, ForehandLeads: true, ForehandOnly: true),

		new(Contract.Three, "tri", GameValue: 10, TalonCards: 3,
			CallsKing: true, IsSolo: false, IsKlop: false, TakesNoTricks: false, TakesAllTricks: false,
			CountsCardPoints: true, ScoresDifference: true, AllowsBonuses: true, UsesNegativePlayRules: false,
			TrumpsArePlainSuit: false, ForehandLeads: true, ForehandOnly: true),

		new(Contract.Two, "dve", GameValue: 20, TalonCards: 2,
			CallsKing: true, IsSolo: false, IsKlop: false, TakesNoTricks: false, TakesAllTricks: false,
			CountsCardPoints: true, ScoresDifference: true, AllowsBonuses: true, UsesNegativePlayRules: false,
			TrumpsArePlainSuit: false, ForehandLeads: true, ForehandOnly: false),

		new(Contract.One, "ena", GameValue: 30, TalonCards: 1,
			CallsKing: true, IsSolo: false, IsKlop: false, TakesNoTricks: false, TakesAllTricks: false,
			CountsCardPoints: true, ScoresDifference: true, AllowsBonuses: true, UsesNegativePlayRules: false,
			TrumpsArePlainSuit: false, ForehandLeads: true, ForehandOnly: false),

		new(Contract.SoloThree, "solo tri", GameValue: 40, TalonCards: 3,
			CallsKing: false, IsSolo: true, IsKlop: false, TakesNoTricks: false, TakesAllTricks: false,
			CountsCardPoints: true, ScoresDifference: true, AllowsBonuses: true, UsesNegativePlayRules: false,
			TrumpsArePlainSuit: false, ForehandLeads: true, ForehandOnly: false),

		new(Contract.SoloTwo, "solo dve", GameValue: 50, TalonCards: 2,
			CallsKing: false, IsSolo: true, IsKlop: false, TakesNoTricks: false, TakesAllTricks: false,
			CountsCardPoints: true, ScoresDifference: true, AllowsBonuses: true, UsesNegativePlayRules: false,
			TrumpsArePlainSuit: false, ForehandLeads: true, ForehandOnly: false),

		new(Contract.SoloOne, "solo ena", GameValue: 60, TalonCards: 1,
			CallsKing: false, IsSolo: true, IsKlop: false, TakesNoTricks: false, TakesAllTricks: false,
			CountsCardPoints: true, ScoresDifference: true, AllowsBonuses: true, UsesNegativePlayRules: false,
			TrumpsArePlainSuit: false, ForehandLeads: true, ForehandOnly: false),

		new(Contract.Beggar, "berač", GameValue: 70, TalonCards: 0,
			CallsKing: false, IsSolo: true, IsKlop: false, TakesNoTricks: true, TakesAllTricks: false,
			CountsCardPoints: false, ScoresDifference: false, AllowsBonuses: false, UsesNegativePlayRules: true,
			TrumpsArePlainSuit: false, ForehandLeads: false, ForehandOnly: false),

		new(Contract.SoloWithout, "solo brez", GameValue: 80, TalonCards: 0,
			CallsKing: false, IsSolo: true, IsKlop: false, TakesNoTricks: false, TakesAllTricks: false,
			CountsCardPoints: true, ScoresDifference: false, AllowsBonuses: false, UsesNegativePlayRules: false,
			TrumpsArePlainSuit: false, ForehandLeads: false, ForehandOnly: false),

		new(Contract.OpenBeggar, "odprti berač", GameValue: 90, TalonCards: 0,
			CallsKing: false, IsSolo: true, IsKlop: false, TakesNoTricks: true, TakesAllTricks: false,
			CountsCardPoints: false, ScoresDifference: false, AllowsBonuses: false, UsesNegativePlayRules: true,
			TrumpsArePlainSuit: false, ForehandLeads: false, ForehandOnly: false),

		new(Contract.ColourValat, "barvni valat", GameValue: 125, TalonCards: 0,
			CallsKing: false, IsSolo: true, IsKlop: false, TakesNoTricks: false, TakesAllTricks: true,
			CountsCardPoints: false, ScoresDifference: false, AllowsBonuses: false, UsesNegativePlayRules: false,
			TrumpsArePlainSuit: true, ForehandLeads: false, ForehandOnly: false),

		new(Contract.Valat, "valat", GameValue: 500, TalonCards: 0,
			CallsKing: false, IsSolo: true, IsKlop: false, TakesNoTricks: false, TakesAllTricks: true,
			CountsCardPoints: false, ScoresDifference: false, AllowsBonuses: false, UsesNegativePlayRules: false,
			TrumpsArePlainSuit: false, ForehandLeads: false, ForehandOnly: false)
	];

	/// <summary>Every contract, lowest bid first.</summary>
	public static IReadOnlyList<ContractInfo> All => Table;

	/// <summary>The lowest contract a player other than forehand may bid.</summary>
	public const Contract LowestOpenBid = Contract.Two;

	/// <summary>The highest contract there is.</summary>
	public const Contract Highest = Contract.Valat;

	public static ContractInfo Info(Contract contract) {
		int index = (int)contract;
		if (index < 0 || index >= Table.Length) {
			throw new ArgumentOutOfRangeException(nameof(contract), contract, "Unknown contract.");
		}

		return Table[index];
	}

	/// <summary>
	/// True for the contracts a declarer may lift to a barvni valat after seeing the talon:
	/// having taken the packet, a solo three, two or one can go for every trick instead.
	/// </summary>
	public static bool CanUpgradeToColourValat(Contract contract) =>
		contract is Contract.SoloThree or Contract.SoloTwo or Contract.SoloOne;

	/// <summary>Contracts from <paramref name="lowest"/> up to valat, in bidding order.</summary>
	public static IReadOnlyList<Contract> From(Contract lowest) {
		List<Contract> contracts = [];
		for (int index = (int)lowest; index <= (int)Highest; index++) {
			contracts.Add((Contract)index);
		}

		return contracts;
	}
}
