using TaroKing.Engine.Cards;

namespace TaroKing.Engine.Tests.Cards;

public class CardTests {

	[Theory]
	[InlineData(SuitRank.King, 5)]
	[InlineData(SuitRank.Queen, 4)]
	[InlineData(SuitRank.Knight, 3)]
	[InlineData(SuitRank.Jack, 2)]
	[InlineData(SuitRank.Pip4, 1)]
	[InlineData(SuitRank.Pip3, 1)]
	[InlineData(SuitRank.Pip2, 1)]
	[InlineData(SuitRank.Pip1, 1)]
	public void Suit_cards_are_worth_their_rank(SuitRank rank, int expected) {
		Assert.Equal(expected, Card.Of(Suit.Hearts, rank).Points);
		Assert.Equal(expected, Card.Of(Suit.Clubs, rank).Points);
	}

	[Fact]
	public void Trula_cards_are_worth_five_and_other_trumps_one() {
		Assert.Equal(5, Card.Pagat.Points);
		Assert.Equal(5, Card.Mond.Points);
		Assert.Equal(5, Card.Skis.Points);

		for (int numeral = 2; numeral <= 20; numeral++) {
			Assert.Equal(1, Card.Trump(numeral).Points);
		}
	}

	[Fact]
	public void Five_pointers_are_exactly_the_kings_and_the_trula() {
		List<Card> fivePointers = Deck.Full().Where(card => card.IsFivePointer).ToList();

		Assert.Equal(7, fivePointers.Count);
		Assert.Equal(4, fivePointers.Count(card => card.IsKing));
		Assert.Equal(3, fivePointers.Count(card => card.IsTrulaCard));
	}

	[Theory]
	[InlineData(Suit.Clubs, SuitRank.Pip1, "7♣")]
	[InlineData(Suit.Clubs, SuitRank.Pip4, "10♣")]
	[InlineData(Suit.Spades, SuitRank.King, "K♠")]
	[InlineData(Suit.Hearts, SuitRank.Pip1, "4♥")]
	[InlineData(Suit.Hearts, SuitRank.Pip4, "1♥")]
	[InlineData(Suit.Diamonds, SuitRank.Knight, "C♦")]
	public void Suit_cards_print_the_label_used_at_the_table(Suit suit, SuitRank rank, string expected) {
		Assert.Equal(expected, Card.Of(suit, rank).ShortName);
	}

	[Theory]
	[InlineData(1, "I")]
	[InlineData(4, "IV")]
	[InlineData(9, "IX")]
	[InlineData(14, "XIV")]
	[InlineData(21, "XXI")]
	public void Trumps_print_as_roman_numerals(int numeral, string expected) {
		Assert.Equal(expected, Card.Trump(numeral).ShortName);
	}

	[Fact]
	public void Skis_prints_by_name() {
		Assert.Equal("Škis", Card.Skis.ShortName);
		Assert.Equal(TarokConstants.TrumpCount, Card.Skis.Rank);
	}

	[Fact]
	public void The_pagat_is_the_lowest_trump_and_the_skis_the_highest() {
		Assert.Equal(1, Card.Pagat.Rank);
		Assert.Equal(21, Card.Mond.Rank);
		Assert.True(Card.Pagat.Rank < Card.Mond.Rank);
		Assert.True(Card.Mond.Rank < Card.Skis.Rank);
	}

	[Fact]
	public void Kings_are_the_strongest_card_in_a_plain_suit() {
		foreach (Suit suit in new[] { Suit.Clubs, Suit.Spades, Suit.Hearts, Suit.Diamonds }) {
			Card king = Card.Of(suit, SuitRank.King);
			Assert.True(king.IsKing);
			Assert.Equal(TarokConstants.SuitSize, king.Rank);
		}
	}

	[Theory]
	[InlineData(Suit.Hearts, 0)]
	[InlineData(Suit.Hearts, 9)]
	[InlineData(Suit.Trump, 0)]
	[InlineData(Suit.Trump, 23)]
	public void Ranks_outside_the_suit_are_rejected(Suit suit, int rank) {
		Assert.Throws<ArgumentOutOfRangeException>(() => new Card(suit, rank));
	}

	[Fact]
	public void Trump_numerals_stop_at_twenty_one() {
		Assert.Throws<ArgumentOutOfRangeException>(() => Card.Trump(22));
		Assert.Throws<ArgumentOutOfRangeException>(() => Card.Trump(0));
	}

	[Fact]
	public void Of_refuses_to_build_a_trump() {
		Assert.Throws<ArgumentOutOfRangeException>(() => Card.Of(Suit.Trump, SuitRank.King));
	}

	[Fact]
	public void Trumps_have_no_suit_rank() {
		Assert.Throws<InvalidOperationException>(() => _ = Card.Skis.AsSuitRank);
		Assert.Equal(SuitRank.King, Card.Of(Suit.Spades, SuitRank.King).AsSuitRank);
	}

	[Fact]
	public void Cards_are_values_and_compare_by_suit_then_rank() {
		Assert.Equal(new Card(Suit.Hearts, 5), Card.Of(Suit.Hearts, SuitRank.Jack));
		Assert.True(Card.Of(Suit.Clubs, SuitRank.King) < Card.Of(Suit.Spades, SuitRank.Pip1));
		Assert.True(Card.Of(Suit.Hearts, SuitRank.Queen) < Card.Of(Suit.Hearts, SuitRank.King));
	}
}
