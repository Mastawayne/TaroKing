using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;
using TaroKing.Engine.Play;
using TaroKing.Engine.Scoring;

namespace TaroKing.Engine.Tests.Scoring;

public class ScoreSheetTests {

	[Fact]
	public void A_fresh_sheet_is_empty() {
		ScoreSheet sheet = new();

		Assert.All(sheet.Totals, total => Assert.Equal(0, total));
		Assert.All(sheet.Radlci, radlci => Assert.Equal(0, radlci));
		Assert.Empty(sheet.Hands);
		Assert.Equal(1, sheet.RadlcMultiplierFor(0));
	}

	[Fact]
	public void Recording_a_hand_moves_everybodys_total() {
		ScoreSheet sheet = new();
		HandScore score = WonSolo();

		sheet.Record(Contract.SoloWithout, declarerSeat: 0, score);

		Assert.Equal(80, sheet.Totals[0]);
		Assert.Equal(-80, sheet.Totals[1]);
		Assert.Equal(-80, sheet.Totals[2]);
		Assert.Equal(-80, sheet.Totals[3]);
		Assert.Single(sheet.Hands);
	}

	[Fact]
	public void Totals_accumulate_across_hands() {
		ScoreSheet sheet = new();

		sheet.Record(Contract.SoloWithout, 0, WonSolo());
		sheet.Record(Contract.SoloWithout, 0, WonSolo());

		Assert.Equal(160, sheet.Totals[0]);
		Assert.Equal(-160, sheet.Totals[1]);
		Assert.Equal(2, sheet.Hands.Count);
	}

	[Fact]
	public void An_ordinary_contract_writes_no_radlci() {
		ScoreSheet sheet = new();

		sheet.Record(Contract.Two, 0, WonSolo());

		Assert.All(sheet.Radlci, radlci => Assert.Equal(0, radlci));
	}

	[Theory]
	[InlineData(Contract.Klop)]
	[InlineData(Contract.Beggar)]
	[InlineData(Contract.SoloWithout)]
	[InlineData(Contract.OpenBeggar)]
	[InlineData(Contract.ColourValat)]
	[InlineData(Contract.Valat)]
	public void Klop_and_beggar_and_above_write_a_radlc_for_everyone(Contract contract) {
		ScoreSheet sheet = new();

		sheet.Record(contract, 0, LostSolo());

		Assert.All(sheet.Radlci, radlci => Assert.Equal(1, radlci));
	}

	[Fact]
	public void A_valat_in_play_writes_radlci_even_in_a_numbered_contract() {
		ScoreSheet sheet = new();

		sheet.Record(Contract.Two, 0, LostSolo(), valatWasInPlay: true);

		Assert.All(sheet.Radlci, radlci => Assert.Equal(1, radlci));
	}

	[Fact]
	public void A_radlc_doubles_the_hand_you_declare() {
		ScoreSheet sheet = new();
		sheet.Record(Contract.Beggar, 0, LostSolo());

		Assert.Equal(2, sheet.RadlcMultiplierFor(0));
		Assert.Equal(2, sheet.RadlcMultiplierFor(1));
	}

	[Fact]
	public void Winning_a_declared_hand_crosses_one_radlc_off() {
		ScoreSheet sheet = new();
		sheet.Record(Contract.Beggar, 0, LostSolo());
		Assert.Equal(1, sheet.Radlci[0]);

		sheet.Record(Contract.Two, declarerSeat: 0, WonSolo());

		Assert.Equal(0, sheet.Radlci[0]);
		Assert.Equal(1, sheet.Radlci[1]);
	}

	[Fact]
	public void Losing_it_leaves_the_radlc_where_it_was() {
		ScoreSheet sheet = new();
		sheet.Record(Contract.Beggar, 0, LostSolo());

		sheet.Record(Contract.Two, declarerSeat: 0, LostSolo());

		Assert.Equal(1, sheet.Radlci[0]);
	}

	[Fact]
	public void Leftover_radlci_cost_a_hundred_each_at_the_end() {
		ScoreSheet sheet = new();
		sheet.Record(Contract.Beggar, 0, LostSolo());
		sheet.Record(Contract.Klop, 0, LostSolo());

		Assert.Equal(2, sheet.Radlci[1]);

		IReadOnlyList<int> final = sheet.FinalTotals();

		Assert.Equal(sheet.Totals[1] - 200, final[1]);
		Assert.Equal(sheet.Totals[0] - 200, final[0]);
	}

	[Fact]
	public void There_are_only_four_seats() {
		ScoreSheet sheet = new();

		Assert.Throws<ArgumentOutOfRangeException>(() => sheet.RadlcMultiplierFor(4));
		Assert.Throws<ArgumentOutOfRangeException>(() => sheet.Record(Contract.Two, 4, WonSolo()));
	}

	// --- helpers ---

	private static HandScore WonSolo() => HandScorer.Score(new HandScoringInput {
		Info = Contracts.Info(Contract.SoloWithout),
		DeclarerSeat = 0,
		DeclaringPile = [
			Card.Of(Suit.Clubs, SuitRank.King), Card.Of(Suit.Spades, SuitRank.King),
			Card.Of(Suit.Hearts, SuitRank.King), Card.Of(Suit.Diamonds, SuitRank.King),
			Card.Skis, Card.Mond, Card.Pagat,
			Card.Of(Suit.Clubs, SuitRank.Queen), Card.Of(Suit.Spades, SuitRank.Queen),
			Card.Of(Suit.Hearts, SuitRank.Queen), Card.Of(Suit.Diamonds, SuitRank.Queen)
		]
	});

	private static HandScore LostSolo() => HandScorer.Score(new HandScoringInput {
		Info = Contracts.Info(Contract.SoloWithout),
		DeclarerSeat = 0,
		DeclaringPile = [Card.Of(Suit.Clubs, SuitRank.Pip1)],
		Tricks = []
	});
}
