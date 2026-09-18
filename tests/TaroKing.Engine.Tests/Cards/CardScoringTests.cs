using TaroKing.Engine.Cards;
using TaroKing.Engine.Randomness;

namespace TaroKing.Engine.Tests.Cards;

public class CardScoringTests {

	[Fact]
	public void Nothing_counts_as_nothing() {
		Assert.Equal(0, CardScoring.Count([]));
		Assert.Equal(0, CardScoring.Deduction(0));
	}

	[Theory]
	[InlineData(1, 1)]
	[InlineData(2, 1)]
	[InlineData(3, 2)]
	[InlineData(4, 3)]
	[InlineData(5, 3)]
	[InlineData(6, 4)]
	[InlineData(12, 8)]
	[InlineData(48, 32)]
	[InlineData(54, 36)]
	public void Every_batch_of_three_costs_two_and_a_remainder_costs_one(int cardCount, int expected) {
		Assert.Equal(expected, CardScoring.Deduction(cardCount));
	}

	[Fact]
	public void Three_kings_count_thirteen() {
		Card[] cards = [
			Card.Of(Suit.Clubs, SuitRank.King),
			Card.Of(Suit.Spades, SuitRank.King),
			Card.Of(Suit.Hearts, SuitRank.King)
		];

		Assert.Equal(15, CardScoring.RawPoints(cards));
		Assert.Equal(13, CardScoring.Count(cards));
	}

	[Fact]
	public void Two_kings_count_nine_and_one_king_counts_four() {
		Card[] two = [Card.Of(Suit.Clubs, SuitRank.King), Card.Of(Suit.Spades, SuitRank.King)];
		Card[] one = [Card.Of(Suit.Clubs, SuitRank.King)];

		Assert.Equal(9, CardScoring.Count(two));
		Assert.Equal(4, CardScoring.Count(one));
	}

	[Fact]
	public void A_trick_of_four_counts_face_value_minus_three() {
		// king 5 + queen 4 + pip 1 + pip 1 = 11 at face value, four cards deduct 3.
		Card[] trick = [
			Card.Of(Suit.Hearts, SuitRank.King),
			Card.Of(Suit.Hearts, SuitRank.Queen),
			Card.Of(Suit.Hearts, SuitRank.Pip1),
			Card.Trump(5)
		];

		Assert.Equal(11, CardScoring.RawPoints(trick));
		Assert.Equal(8, CardScoring.Count(trick));
	}

	[Fact]
	public void The_trula_counts_thirteen() {
		Card[] trula = [Card.Skis, Card.Mond, Card.Pagat];

		Assert.Equal(13, CardScoring.Count(trula));
	}

	[Fact]
	public void The_whole_pack_counts_seventy() {
		Assert.Equal(TarokConstants.TotalCardPoints, CardScoring.Count(Deck.Full()));
	}

	[Fact]
	public void The_two_sides_of_a_finished_hand_always_add_up_to_seventy() {
		// 48 cards in tricks plus the 6 talon cards, split at an arbitrary point.
		Card[] pack = Deck.Shuffled(seed: 4242);
		Card[] declarer = pack.Take(24).ToArray();
		Card[] opponents = pack.Skip(24).ToArray();

		Assert.Equal(TarokConstants.TotalCardPoints, CardScoring.Count(declarer) + CardScoring.Count(opponents));
	}

	[Fact]
	public void Counting_does_not_depend_on_the_order_of_the_cards() {
		Card[] cards = Deck.Shuffled(seed: 11).Take(20).ToArray();
		int counted = CardScoring.Count(cards);

		Pcg32 random = new(seed: 55);
		for (int i = 0; i < 20; i++) {
			random.Shuffle(cards);
			Assert.Equal(counted, CardScoring.Count(cards));
		}
	}
}
