using TaroKing.Engine.Announcing;
using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;
using TaroKing.Engine.Play;

namespace TaroKing.Engine.Scoring;

/// <summary>Everything the scorer needs to know about a finished hand.</summary>
public sealed record HandScoringInput {

	public required ContractInfo Info { get; init; }

	public required int DeclarerSeat { get; init; }

	/// <summary>The called king's holder, when there is one and it is not the declarer.</summary>
	public int? PartnerSeat { get; init; }

	/// <summary>Everything the declaring side took in, including the declarer's lay-away.</summary>
	public IReadOnlyList<Card> DeclaringPile { get; init; } = [];

	/// <summary>Everything the defenders took in, including the talon packets left over.</summary>
	public IReadOnlyList<Card> DefendingPile { get; init; } = [];

	public IReadOnlyList<Trick> Tricks { get; init; } = [];

	public IReadOnlyList<int> CapturedMondSeats { get; init; } = [];

	public AnnouncementRound? Announcements { get; init; }

	public Card? CalledKing { get; init; }

	/// <summary>Per-seat piles, used only by klop where every player counts for themselves.</summary>
	public IReadOnlyList<IReadOnlyList<Card>> KlopPiles { get; init; } = [];

	/// <summary>2 when the declarer carries an uncancelled radlc, otherwise 1.</summary>
	public int RadlcMultiplier { get; init; } = 1;
}

/// <summary>
/// Turns a finished hand into score-sheet lines.
///
/// Every line is written from the declaring side's point of view: a positive total means points to
/// the declarer's side, a negative one means points to the defenders. Each player on the winning
/// side then writes that amount and each player on the losing side writes it as a minus, which is
/// how a solo ends up worth more than it costs any single opponent.
///
/// The two personal items sit outside that: the captured mond costs its own player 20, and klop is
/// scored seat by seat with no sides at all.
/// </summary>
public static class HandScorer {

	/// <summary>Rounds to the nearest 5, halves going away from zero. 8 → 10, 2 → 0, −14 → −15.</summary>
	public static int RoundToFive(int value) {
		int sign = Math.Sign(value);
		int magnitude = Math.Abs(value);
		return sign * (magnitude + 2) / 5 * 5;
	}

	public static HandScore Score(HandScoringInput input) {
		ArgumentNullException.ThrowIfNull(input);

		return input.Info.IsKlop ? ScoreKlop(input) : ScoreContract(input);
	}

	private static HandScore ScoreContract(HandScoringInput input) {
		ContractInfo info = input.Info;
		int[] sideOfSeat = SidesOfSeats(input);
		List<ScoreLine> lines = [];

		int gameMultiplier =
			(input.Announcements?.Multiplier(KontraTarget.Game) ?? 1) * input.RadlcMultiplier;

		int cardPoints = 0;
		int difference = 0;
		bool won;

		if (info.CountsCardPoints) {
			cardPoints = CardScoring.Count(input.DeclaringPile);
			won = cardPoints >= TarokConstants.WinningCardPoints;
			difference = info.ScoresDifference
				? RoundToFive(cardPoints - TarokConstants.DifferenceBaseline)
				: 0;

			lines.Add(new ScoreLine(info.SlovenianName, (won ? info.GameValue : -info.GameValue) + difference, gameMultiplier));
		} else if (info.TakesNoTricks) {
			won = TricksWonBy(input, sideOfSeat, Sides.Declaring) == 0;
			lines.Add(new ScoreLine(info.SlovenianName, won ? info.GameValue : -info.GameValue, gameMultiplier));
		} else {
			int declaringTricks = TricksWonBy(input, sideOfSeat, Sides.Declaring);
			won = input.Tricks.Count > 0 && declaringTricks == input.Tricks.Count;
			lines.Add(new ScoreLine(info.SlovenianName, won ? info.GameValue : -info.GameValue, gameMultiplier));
		}

		if (info.AllowsBonuses) {
			AddBonusLines(input, sideOfSeat, lines);
		}

		int declaringTotal = lines.Sum(line => line.Total);
		int[] bySeat = new int[TarokConstants.PlayerCount];

		for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
			bySeat[seat] = sideOfSeat[seat] == Sides.Declaring ? declaringTotal : -declaringTotal;
		}

		foreach (int seat in input.CapturedMondSeats) {
			bySeat[seat] += TarokConstants.CapturedMondPenalty;
		}

		return new HandScore(lines, bySeat, cardPoints, difference, won);
	}

	/// <summary>Klop: every player for themselves. No trick at all is +70, 36 points or more is −70,
	/// and anything between costs the points taken, rounded to the nearest 5.</summary>
	private static HandScore ScoreKlop(HandScoringInput input) {
		List<ScoreLine> lines = [];
		int[] bySeat = new int[TarokConstants.PlayerCount];

		for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
			IReadOnlyList<Card> pile = seat < input.KlopPiles.Count ? input.KlopPiles[seat] : [];
			int tricks = input.Tricks.Count(trick => trick.WinnerSeat == seat);
			int points = CardScoring.Count(pile);

			int value = tricks == 0
				? input.Info.GameValue
				: points >= TarokConstants.WinningCardPoints
					? -input.Info.GameValue
					: -RoundToFive(points);

			bySeat[seat] = value;
			lines.Add(new ScoreLine($"klop (sedež {seat})", value));
		}

		return new HandScore(lines, bySeat, cardPoints: 0, difference: 0, declarerWon: false);
	}

	private static void AddBonusLines(HandScoringInput input, int[] sideOfSeat, List<ScoreLine> lines) {
		AnnouncementRound? round = input.Announcements;
		Dictionary<Bonus, int?> achieved = ResolveBonuses(input, sideOfSeat);
		bool valatHappened = achieved[Bonus.Valat] is not null;

		foreach (BonusInfo bonus in Bonuses.All) {
			// A valat sweeps the table: nothing else is worth anything next to it.
			if (valatHappened && bonus.Bonus != Bonus.Valat) {
				continue;
			}

			int? achievedBy = achieved[bonus.Bonus];
			bool announced = false;

			for (int side = Sides.Declaring; side <= Sides.Defending; side++) {
				if (round?.WasAnnounced(bonus.Bonus, side) != true) {
					continue;
				}

				announced = true;
				int multiplier = round.Multiplier(new KontraTarget(bonus.Bonus, side)) * input.RadlcMultiplier;
				int sign = achievedBy == side ? 1 : -1;
				int perspective = side == Sides.Declaring ? 1 : -1;

				lines.Add(new ScoreLine(
					$"{bonus.SlovenianName} ({SideName(side)})",
					bonus.AnnouncedValue * sign * perspective,
					multiplier));
			}

			if (!announced && achievedBy is not null) {
				lines.Add(new ScoreLine(
					$"{bonus.SlovenianName} (tiho)",
					bonus.SilentValue * (achievedBy == Sides.Declaring ? 1 : -1),
					input.RadlcMultiplier));
			}
		}
	}

	private static Dictionary<Bonus, int?> ResolveBonuses(HandScoringInput input, int[] sideOfSeat) {
		Dictionary<Bonus, int?> achieved = new() {
			[Bonus.Trula] = SideHoldingAll(input, card => card.IsTrulaCard, 3),
			[Bonus.Kings] = SideHoldingAll(input, card => card.IsKing, 4),
			[Bonus.Valat] = SideWithEveryTrick(input, sideOfSeat),
			[Bonus.PagatUltimo] = SideWinningLastTrickWith(input, sideOfSeat, card => card.IsPagat),
			[Bonus.KingUltimo] = input.CalledKing is null
				? null
				: SideWinningLastTrickWith(input, sideOfSeat, card => card == input.CalledKing.Value)
		};

		return achieved;
	}

	private static int? SideHoldingAll(HandScoringInput input, Func<Card, bool> matches, int count) {
		if (input.DeclaringPile.Count(matches) == count) {
			return Sides.Declaring;
		}

		return input.DefendingPile.Count(matches) == count ? Sides.Defending : null;
	}

	private static int? SideWithEveryTrick(HandScoringInput input, int[] sideOfSeat) {
		if (input.Tricks.Count == 0) {
			return null;
		}

		int declaring = TricksWonBy(input, sideOfSeat, Sides.Declaring);
		if (declaring == input.Tricks.Count) {
			return Sides.Declaring;
		}

		return declaring == 0 ? Sides.Defending : null;
	}

	private static int? SideWinningLastTrickWith(HandScoringInput input, int[] sideOfSeat, Func<Card, bool> matches) {
		if (input.Tricks.Count != TarokConstants.TrickCount) {
			return null;
		}

		Trick last = input.Tricks[^1];
		return matches(last.WinningCard) ? sideOfSeat[last.WinnerSeat] : null;
	}

	private static int TricksWonBy(HandScoringInput input, int[] sideOfSeat, int side) =>
		input.Tricks.Count(trick => sideOfSeat[trick.WinnerSeat] == side);

	private static int[] SidesOfSeats(HandScoringInput input) {
		int[] sides = new int[TarokConstants.PlayerCount];

		for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
			sides[seat] = seat == input.DeclarerSeat || seat == input.PartnerSeat
				? Sides.Declaring
				: Sides.Defending;
		}

		return sides;
	}

	private static string SideName(int side) => side == Sides.Declaring ? "igralec" : "nasprotniki";
}
