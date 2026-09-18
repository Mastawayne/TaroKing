using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;
using TaroKing.Engine.Play;

namespace TaroKing.Engine.Tests.Play;

public class TrickRulesTests {

	private static readonly ContractInfo Positive = Contracts.Info(Contract.Three);
	private static readonly ContractInfo Klop = Contracts.Info(Contract.Klop);
	private static readonly ContractInfo Beggar = Contracts.Info(Contract.Beggar);
	private static readonly ContractInfo ColourValat = Contracts.Info(Contract.ColourValat);

	// --- following suit ---

	[Fact]
	public void The_led_suit_has_to_be_followed() {
		Card[] hand = [
			Card.Of(Suit.Hearts, SuitRank.Jack),
			Card.Of(Suit.Hearts, SuitRank.King),
			Card.Of(Suit.Clubs, SuitRank.Queen),
			Card.Trump(10)
		];

		IReadOnlyList<Card> legal = TrickRules.LegalPlays(Positive, hand, Led(Card.Of(Suit.Hearts, SuitRank.Knight)));

		Assert.Equal(2, legal.Count);
		Assert.All(legal, card => Assert.Equal(Suit.Hearts, card.Suit));
	}

	[Fact]
	public void Without_the_led_suit_you_must_trump() {
		Card[] hand = [
			Card.Of(Suit.Clubs, SuitRank.Queen),
			Card.Trump(3),
			Card.Trump(10)
		];

		IReadOnlyList<Card> legal = TrickRules.LegalPlays(Positive, hand, Led(Card.Of(Suit.Hearts, SuitRank.Knight)));

		Assert.Equal(2, legal.Count);
		Assert.All(legal, card => Assert.True(card.IsTrump));
	}

	[Fact]
	public void With_neither_the_suit_nor_a_trump_anything_goes() {
		Card[] hand = [
			Card.Of(Suit.Clubs, SuitRank.Queen),
			Card.Of(Suit.Spades, SuitRank.Pip1)
		];

		IReadOnlyList<Card> legal = TrickRules.LegalPlays(Positive, hand, Led(Card.Of(Suit.Hearts, SuitRank.Knight)));

		Assert.Equal(2, legal.Count);
	}

	[Fact]
	public void The_leader_may_play_anything() {
		Card[] hand = [Card.Of(Suit.Clubs, SuitRank.Queen), Card.Trump(3), Card.Pagat];

		Assert.Equal(3, TrickRules.LegalPlays(Positive, hand, []).Count);
	}

	// --- who takes it ---

	[Fact]
	public void The_highest_trump_takes_the_trick() {
		TrickCard[] trick = [
			new(0, Card.Of(Suit.Hearts, SuitRank.King)),
			new(1, Card.Trump(4)),
			new(2, Card.Trump(18)),
			new(3, Card.Of(Suit.Hearts, SuitRank.Queen))
		];

		Assert.Equal(2, TrickRules.Winner(Positive, trick));
		Assert.Equal(Card.Trump(18), TrickRules.WinningCard(Positive, trick));
	}

	[Fact]
	public void Without_a_trump_the_highest_card_of_the_led_suit_takes_it() {
		TrickCard[] trick = [
			new(0, Card.Of(Suit.Hearts, SuitRank.Jack)),
			new(1, Card.Of(Suit.Hearts, SuitRank.King)),
			new(2, Card.Of(Suit.Clubs, SuitRank.King)),
			new(3, Card.Of(Suit.Hearts, SuitRank.Queen))
		];

		Assert.Equal(1, TrickRules.Winner(Positive, trick));
	}

	[Fact]
	public void A_part_played_trick_already_has_a_leader() {
		TrickCard[] trick = [
			new(0, Card.Of(Suit.Hearts, SuitRank.Jack)),
			new(1, Card.Of(Suit.Hearts, SuitRank.Queen))
		];

		Assert.Equal(1, TrickRules.Winner(Positive, trick));
	}

	// --- the emperor trick ---

	[Fact]
	public void Skis_mond_and_pagat_together_hand_the_trick_to_the_pagat() {
		TrickCard[] trick = [
			new(0, Card.Mond),
			new(1, Card.Skis),
			new(2, Card.Pagat),
			new(3, Card.Of(Suit.Hearts, SuitRank.King))
		];

		Assert.True(TrickRules.IsEmperorTrick(trick));
		Assert.Equal(2, TrickRules.Winner(Positive, trick));
	}

	[Fact]
	public void Two_of_the_three_is_not_an_emperor_trick() {
		TrickCard[] trick = [
			new(0, Card.Mond),
			new(1, Card.Skis),
			new(2, Card.Trump(7)),
			new(3, Card.Of(Suit.Hearts, SuitRank.King))
		];

		Assert.False(TrickRules.IsEmperorTrick(trick));
		Assert.Equal(1, TrickRules.Winner(Positive, trick));
	}

	// --- the captured mond ---

	[Fact]
	public void Losing_the_mond_to_the_skis_is_recorded_against_its_player() {
		TrickCard[] trick = [
			new(0, Card.Mond),
			new(1, Card.Skis),
			new(2, Card.Trump(7)),
			new(3, Card.Of(Suit.Hearts, SuitRank.King))
		];

		Assert.Equal(0, TrickRules.CapturedMondSeat(Positive, trick));
	}

	[Fact]
	public void The_penalty_stands_even_when_the_pagat_steals_the_trick() {
		TrickCard[] trick = [
			new(0, Card.Mond),
			new(1, Card.Skis),
			new(2, Card.Pagat),
			new(3, Card.Of(Suit.Hearts, SuitRank.King))
		];

		Assert.Equal(0, TrickRules.CapturedMondSeat(Positive, trick));
	}

	[Fact]
	public void No_skis_no_penalty() {
		TrickCard[] trick = [
			new(0, Card.Mond),
			new(1, Card.Trump(7)),
			new(2, Card.Trump(2)),
			new(3, Card.Of(Suit.Hearts, SuitRank.King))
		];

		Assert.Null(TrickRules.CapturedMondSeat(Positive, trick));
	}

	[Fact]
	public void Contracts_without_card_points_carry_no_mond_penalty() {
		TrickCard[] trick = [
			new(0, Card.Mond),
			new(1, Card.Skis),
			new(2, Card.Trump(7)),
			new(3, Card.Of(Suit.Hearts, SuitRank.King))
		];

		Assert.Null(TrickRules.CapturedMondSeat(Klop, trick));
		Assert.Null(TrickRules.CapturedMondSeat(Beggar, trick));
		Assert.Null(TrickRules.CapturedMondSeat(ColourValat, trick));
	}

	// --- negative contracts ---

	[Fact]
	public void In_a_negative_contract_you_must_beat_the_table() {
		Card[] hand = [
			Card.Of(Suit.Hearts, SuitRank.Jack),
			Card.Of(Suit.Hearts, SuitRank.Queen),
			Card.Of(Suit.Hearts, SuitRank.Pip1)
		];

		IReadOnlyList<Card> legal = TrickRules.LegalPlays(Klop, hand, Led(Card.Of(Suit.Hearts, SuitRank.Knight)));

		Assert.Equal(new[] { Card.Of(Suit.Hearts, SuitRank.Queen) }, legal);
	}

	[Fact]
	public void When_you_cannot_beat_it_you_are_free_within_the_suit() {
		Card[] hand = [
			Card.Of(Suit.Hearts, SuitRank.Jack),
			Card.Of(Suit.Hearts, SuitRank.Pip1),
			Card.Of(Suit.Hearts, SuitRank.Pip2)
		];

		IReadOnlyList<Card> legal = TrickRules.LegalPlays(Klop, hand, Led(Card.Of(Suit.Hearts, SuitRank.King)));

		Assert.Equal(3, legal.Count);
	}

	[Fact]
	public void Overtrumping_is_compulsory_in_a_negative_contract_too() {
		Card[] hand = [Card.Trump(4), Card.Trump(12), Card.Of(Suit.Clubs, SuitRank.King)];

		IReadOnlyList<Card> legal = TrickRules.LegalPlays(Beggar, hand, Led(Card.Trump(8)));

		Assert.Equal(new[] { Card.Trump(12) }, legal);
	}

	[Fact]
	public void The_pagat_is_held_back_while_anything_else_is_legal() {
		Card[] hand = [Card.Pagat, Card.Trump(2)];

		IReadOnlyList<Card> legal = TrickRules.LegalPlays(Klop, hand, Led(Card.Trump(9)));

		Assert.Equal(new[] { Card.Trump(2) }, legal);
	}

	[Fact]
	public void The_pagat_comes_out_when_there_is_nothing_else() {
		Card[] hand = [Card.Pagat];

		Assert.Equal(new[] { Card.Pagat }, TrickRules.LegalPlays(Klop, hand, Led(Card.Trump(9))));
		Assert.Equal(new[] { Card.Pagat }, TrickRules.LegalPlays(Klop, hand, Array.Empty<TrickCard>()));
	}

	[Fact]
	public void The_pagat_is_free_in_an_ordinary_contract() {
		Card[] hand = [Card.Pagat, Card.Trump(2)];

		Assert.Equal(2, TrickRules.LegalPlays(Positive, hand, Led(Card.Trump(9))).Count);
	}

	// --- colour valat ---

	[Fact]
	public void Colour_valat_never_forces_a_trump() {
		Card[] hand = [Card.Of(Suit.Hearts, SuitRank.King), Card.Trump(5)];

		IReadOnlyList<Card> legal = TrickRules.LegalPlays(ColourValat, hand, Led(Card.Of(Suit.Clubs, SuitRank.King)));

		Assert.Equal(2, legal.Count);
	}

	[Fact]
	public void Colour_valat_still_makes_you_follow_a_trump_lead() {
		Card[] hand = [Card.Of(Suit.Hearts, SuitRank.King), Card.Trump(5)];

		IReadOnlyList<Card> legal = TrickRules.LegalPlays(ColourValat, hand, Led(Card.Trump(9)));

		Assert.Equal(new[] { Card.Trump(5) }, legal);
	}

	[Fact]
	public void A_trump_thrown_on_a_suit_lead_wins_nothing_in_colour_valat() {
		TrickCard[] trick = [
			new(0, Card.Of(Suit.Hearts, SuitRank.Jack)),
			new(1, Card.Skis),
			new(2, Card.Of(Suit.Hearts, SuitRank.Queen)),
			new(3, Card.Mond)
		];

		Assert.Equal(2, TrickRules.Winner(ColourValat, trick));
		Assert.Equal(1, TrickRules.Winner(Positive, trick));
	}

	[Fact]
	public void A_trump_lead_in_colour_valat_is_won_by_the_highest_trump() {
		TrickCard[] trick = [
			new(0, Card.Trump(4)),
			new(1, Card.Trump(11)),
			new(2, Card.Of(Suit.Hearts, SuitRank.King)),
			new(3, Card.Trump(6))
		];

		Assert.Equal(1, TrickRules.Winner(ColourValat, trick));
	}

	private static TrickCard[] Led(Card card) => [new TrickCard(0, card)];
}
