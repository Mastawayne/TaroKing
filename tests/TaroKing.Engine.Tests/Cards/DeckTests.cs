using TaroKing.Engine.Cards;

namespace TaroKing.Engine.Tests.Cards;

public class DeckTests {

	[Fact]
	public void The_pack_holds_fifty_four_distinct_cards() {
		IReadOnlyList<Card> pack = Deck.Full();

		Assert.Equal(TarokConstants.DeckSize, pack.Count);
		Assert.Equal(TarokConstants.DeckSize, pack.Distinct().Count());
	}

	[Fact]
	public void The_pack_holds_four_suits_of_eight_and_twenty_two_trumps() {
		IReadOnlyList<Card> pack = Deck.Full();

		foreach (Suit suit in new[] { Suit.Clubs, Suit.Spades, Suit.Hearts, Suit.Diamonds }) {
			Assert.Equal(TarokConstants.SuitSize, pack.Count(card => card.Suit == suit));
		}

		Assert.Equal(TarokConstants.TrumpCount, pack.Count(card => card.IsTrump));
	}

	[Fact]
	public void The_pack_holds_four_kings_and_one_of_each_trula_card() {
		IReadOnlyList<Card> pack = Deck.Full();

		Assert.Equal(4, pack.Count(card => card.IsKing));
		Assert.Equal(1, pack.Count(card => card.IsSkis));
		Assert.Equal(1, pack.Count(card => card.IsMond));
		Assert.Equal(1, pack.Count(card => card.IsPagat));
	}

	[Fact]
	public void The_pack_is_worth_seventy_counted_and_one_hundred_and_six_at_face_value() {
		Assert.Equal(106, CardScoring.RawPoints(Deck.Full()));
		Assert.Equal(TarokConstants.TotalCardPoints, CardScoring.Count(Deck.Full()));
	}

	[Fact]
	public void Shuffling_keeps_every_card() {
		Card[] shuffled = Deck.Shuffled(seed: 12345);

		Assert.Equal(TarokConstants.DeckSize, shuffled.Length);
		Assert.Equal(Deck.Full().OrderBy(card => card), shuffled.OrderBy(card => card));
	}

	[Fact]
	public void The_same_seed_shuffles_the_same_way() {
		Assert.Equal(Deck.Shuffled(seed: 7), Deck.Shuffled(seed: 7));
	}

	[Fact]
	public void Different_seeds_shuffle_differently() {
		Assert.NotEqual(Deck.Shuffled(seed: 1), Deck.Shuffled(seed: 2));
	}

	[Fact]
	public void Shuffling_does_not_disturb_the_canonical_pack() {
		Card[] before = Deck.Full().ToArray();
		_ = Deck.Shuffled(seed: 99);

		Assert.Equal(before, Deck.Full());
	}
}
