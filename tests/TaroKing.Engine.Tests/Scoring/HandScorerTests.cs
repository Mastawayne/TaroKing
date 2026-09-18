using TaroKing.Engine.Announcing;
using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;
using TaroKing.Engine.Play;
using TaroKing.Engine.Scoring;

namespace TaroKing.Engine.Tests.Scoring;

public class HandScorerTests {

	private static readonly Card CalledKing = Card.Of(Suit.Hearts, SuitRank.King);

	[Theory]
	[InlineData(0, 0)]
	[InlineData(2, 0)]
	[InlineData(3, 5)]
	[InlineData(8, 10)]
	[InlineData(12, 10)]
	[InlineData(13, 15)]
	[InlineData(-2, 0)]
	[InlineData(-8, -10)]
	[InlineData(-14, -15)]
	public void The_difference_rounds_to_the_nearest_five(int raw, int expected) {
		Assert.Equal(expected, HandScorer.RoundToFive(raw));
	}

	[Fact]
	public void A_flat_contract_is_won_on_thirty_six_card_points() {
		HandScore score = Score(Contract.SoloWithout, RichPile());

		Assert.True(score.DeclarerWon);
		Assert.Equal(80, score.DeclaringTotal);
		Assert.Equal(0, score.Difference);

		// The declarer is alone: each of the other three writes the same amount as a minus.
		Assert.Equal(80, score[0]);
		Assert.Equal(-80, score[1]);
		Assert.Equal(-80, score[2]);
		Assert.Equal(-80, score[3]);
	}

	[Fact]
	public void A_flat_contract_is_lost_below_thirty_six() {
		HandScore score = Score(Contract.SoloWithout, PoorPile());

		Assert.False(score.DeclarerWon);
		Assert.Equal(-80, score.DeclaringTotal);
		Assert.Equal(-80, score[0]);
		Assert.Equal(80, score[1]);
	}

	[Fact]
	public void A_numbered_contract_adds_the_rounded_difference() {
		// The pile counts 44, so the difference is 9, rounded to 10, on top of the game's 20.
		HandScore score = Score(Contract.Two, RichPile(), partnerSeat: 2);

		Assert.Equal(44, score.CardPoints);
		Assert.Equal(10, score.Difference);

		ScoreLine game = score.Lines[0];
		Assert.Equal(30, game.Total);
	}

	[Fact]
	public void The_partner_scores_with_the_declarer_and_the_defenders_against() {
		HandScore score = Score(Contract.Two, RichPile(), partnerSeat: 2);

		Assert.Equal(score[0], score[2]);
		Assert.Equal(-score[0], score[1]);
		Assert.Equal(-score[0], score[3]);
	}

	[Fact]
	public void The_trula_and_the_kings_score_quietly_when_nobody_announced_them() {
		HandScore score = Score(Contract.Two, RichPile(), partnerSeat: 2);

		Assert.Contains(score.Lines, line => line.Name.StartsWith("trula") && line.Total == 10);
		Assert.Contains(score.Lines, line => line.Name.StartsWith("kralji") && line.Total == 10);

		// 20 game + 10 difference + 10 trula + 10 kings.
		Assert.Equal(50, score.DeclaringTotal);
	}

	[Fact]
	public void An_announced_trula_is_worth_double_and_costs_double_when_it_fails() {
		AnnouncementRound round = Round();
		round.Announce(0, Bonus.Trula);
		PassToEnd(round, 0);

		HandScore won = Score(Contract.Two, RichPile(), partnerSeat: 2, announcements: round);
		Assert.Contains(won.Lines, line => line.Name.StartsWith("trula") && line.Total == 20);

		HandScore failed = Score(Contract.Two, PoorPile(), partnerSeat: 2, announcements: round);
		Assert.Contains(failed.Lines, line => line.Name.StartsWith("trula") && line.Total == -20);
	}

	[Fact]
	public void A_kontra_doubles_the_game_but_leaves_the_bonuses_alone() {
		AnnouncementRound round = Round();
		round.Announce(0, Bonus.Trula);
		round.Pass(0);
		round.Kontra(1, KontraTarget.Game);
		PassToEnd(round, 1);

		HandScore score = Score(Contract.Two, RichPile(), partnerSeat: 2, announcements: round);

		Assert.Equal(60, score.Lines[0].Total);
		Assert.Equal(2, score.Lines[0].Multiplier);
		Assert.Contains(score.Lines, line => line.Name.StartsWith("trula") && line.Multiplier == 1);
	}

	[Fact]
	public void A_bonus_taken_by_the_defenders_counts_against_the_declarer() {
		// The defenders take the last trick with the pagat, unannounced: 25 to them.
		List<Trick> tricks = FullHand(winner: 0);
		tricks[^1] = TrickWonBy(TarokConstants.TrickCount, winner: 1, winningCard: Card.Pagat);

		HandScore score = Score(Contract.Two, RichPile(), partnerSeat: 2, tricks: tricks);

		Assert.Contains(score.Lines, line => line.Name.StartsWith("pagat ultimo") && line.Total == -25);
	}

	[Fact]
	public void King_ultimo_belongs_to_whoever_wins_the_last_trick_with_the_called_king() {
		// A defender takes one trick, so this is a kralj ultimo and not a valat.
		List<Trick> tricks = FullHand(winner: 0);
		tricks[0] = TrickWonBy(1, winner: 1);
		tricks[^1] = TrickWonBy(TarokConstants.TrickCount, winner: 2, winningCard: CalledKing);

		HandScore score = Score(Contract.Two, RichPile(), partnerSeat: 2, tricks: tricks, calledKing: CalledKing);

		Assert.Contains(score.Lines, line => line.Name.StartsWith("kralj ultimo") && line.Total == 10);
	}

	[Fact]
	public void A_valat_sweeps_every_other_bonus_off_the_sheet() {
		HandScore score = Score(Contract.Two, RichPile(), partnerSeat: 2, tricks: FullHand(winner: 0));

		Assert.Contains(score.Lines, line => line.Name.StartsWith("valat") && line.Total == 250);
		Assert.DoesNotContain(score.Lines, line => line.Name.StartsWith("trula"));
		Assert.DoesNotContain(score.Lines, line => line.Name.StartsWith("kralji"));
	}

	[Fact]
	public void The_captured_mond_costs_its_own_player_twenty() {
		HandScore plain = Score(Contract.Two, RichPile(), partnerSeat: 2);
		HandScore penalised = Score(Contract.Two, RichPile(), partnerSeat: 2, capturedMondSeats: [1]);

		Assert.Equal(plain[1] - 20, penalised[1]);
		Assert.Equal(plain[0], penalised[0]);
	}

	[Fact]
	public void A_beggar_is_won_by_taking_nothing() {
		HandScore won = Score(Contract.Beggar, [], tricks: FullHand(winner: 1));
		Assert.True(won.DeclarerWon);
		Assert.Equal(70, won.DeclaringTotal);

		HandScore lost = Score(Contract.Beggar, [], tricks: FullHand(winner: 0));
		Assert.False(lost.DeclarerWon);
		Assert.Equal(-70, lost.DeclaringTotal);
	}

	[Fact]
	public void A_valat_contract_needs_every_last_trick() {
		HandScore won = Score(Contract.Valat, [], tricks: FullHand(winner: 0));
		Assert.Equal(500, won.DeclaringTotal);

		List<Trick> nearly = FullHand(winner: 0);
		nearly[5] = TrickWonBy(6, winner: 3);

		HandScore lost = Score(Contract.Valat, [], tricks: nearly);
		Assert.Equal(-500, lost.DeclaringTotal);
	}

	[Fact]
	public void Klop_scores_every_player_on_their_own() {
		// Seat 0 takes nothing, seat 1 takes a fat pile, seats 2 and 3 take a little.
		List<Trick> tricks = [
			TrickWonBy(1, 1), TrickWonBy(2, 1), TrickWonBy(3, 1), TrickWonBy(4, 1),
			TrickWonBy(5, 1), TrickWonBy(6, 1), TrickWonBy(7, 2), TrickWonBy(8, 2),
			TrickWonBy(9, 3), TrickWonBy(10, 3), TrickWonBy(11, 3), TrickWonBy(12, 3)
		];

		IReadOnlyList<Card>[] piles = [
			[],
			RichPile(),
			[Card.Of(Suit.Clubs, SuitRank.Queen), Card.Of(Suit.Clubs, SuitRank.Jack), Card.Of(Suit.Clubs, SuitRank.Pip1)],
			[Card.Of(Suit.Spades, SuitRank.Pip1), Card.Of(Suit.Spades, SuitRank.Pip2), Card.Of(Suit.Spades, SuitRank.Pip3)]
		];

		HandScore score = HandScorer.Score(new HandScoringInput {
			Info = Contracts.Info(Contract.Klop),
			DeclarerSeat = 0,
			Tricks = tricks,
			KlopPiles = piles
		});

		Assert.Equal(70, score[0]);    // no tricks at all
		Assert.Equal(-70, score[1]);   // 44 points, well over 35
		Assert.Equal(-5, score[2]);    // 4+2+1 = 7 raw, less 2 = 5
		Assert.Equal(0, score[3]);     // 3 raw, less 2 = 1, rounds to 0
	}

	// --- helpers ---

	/// <summary>Four kings, the trula and four queens: 44 card points, so the trula and kings are in there too.</summary>
	private static Card[] RichPile() => [
		Card.Of(Suit.Clubs, SuitRank.King), Card.Of(Suit.Spades, SuitRank.King),
		Card.Of(Suit.Hearts, SuitRank.King), Card.Of(Suit.Diamonds, SuitRank.King),
		Card.Skis, Card.Mond, Card.Pagat,
		Card.Of(Suit.Clubs, SuitRank.Queen), Card.Of(Suit.Spades, SuitRank.Queen),
		Card.Of(Suit.Hearts, SuitRank.Queen), Card.Of(Suit.Diamonds, SuitRank.Queen)
	];

	/// <summary>Three low cards: one card point once the batch deduction bites.</summary>
	private static Card[] PoorPile() => [
		Card.Of(Suit.Clubs, SuitRank.Pip1), Card.Of(Suit.Clubs, SuitRank.Pip2), Card.Of(Suit.Clubs, SuitRank.Pip3)
	];

	private static HandScore Score(
		Contract contract,
		IReadOnlyList<Card> declaringPile,
		int? partnerSeat = null,
		IReadOnlyList<Trick>? tricks = null,
		AnnouncementRound? announcements = null,
		Card? calledKing = null,
		IReadOnlyList<int>? capturedMondSeats = null,
		int radlcMultiplier = 1) {

		return HandScorer.Score(new HandScoringInput {
			Info = Contracts.Info(contract),
			DeclarerSeat = 0,
			PartnerSeat = partnerSeat,
			DeclaringPile = declaringPile,
			DefendingPile = [],
			Tricks = tricks ?? [],
			Announcements = announcements,
			CalledKing = calledKing,
			CapturedMondSeats = capturedMondSeats ?? [],
			RadlcMultiplier = radlcMultiplier
		});
	}

	private static Trick TrickWonBy(int number, int winner, Card? winningCard = null) {
		Card[] fillers = [Card.Trump(2), Card.Trump(3), Card.Trump(4), Card.Trump(5)];
		List<TrickCard> cards = [];

		for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
			Card card = seat == winner && winningCard is not null ? winningCard.Value : fillers[seat];
			cards.Add(new TrickCard(seat, card));
		}

		return new Trick(number, 0, cards, winner);
	}

	private static List<Trick> FullHand(int winner) {
		List<Trick> tricks = [];
		for (int number = 1; number <= TarokConstants.TrickCount; number++) {
			tricks.Add(TrickWonBy(number, winner));
		}

		return tricks;
	}

	private static AnnouncementRound Round() {
		IReadOnlyList<Card>[] hands = [
			[Card.Mond], [Card.Pagat], [CalledKing], [Card.Skis]
		];

		return AnnouncementRound.Start(
			Contracts.Info(Contract.Two),
			[Sides.Declaring, Sides.Defending, Sides.Declaring, Sides.Defending],
			declarerSeat: 0,
			hands,
			CalledKing);
	}

	private static void PassToEnd(AnnouncementRound round, int fromSeat) {
		int seat = fromSeat;
		while (!round.IsComplete) {
			round.Pass(seat);
			seat = (seat + 1) % TarokConstants.PlayerCount;
		}
	}
}
