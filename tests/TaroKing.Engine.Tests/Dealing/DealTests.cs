using TaroKing.Engine.Cards;
using TaroKing.Engine.Dealing;

namespace TaroKing.Engine.Tests.Dealing;

public class DealTests {

	[Fact]
	public void A_deal_puts_six_in_the_talon_and_twelve_in_every_hand() {
		Deal deal = Deal.Create(seed: 1);

		Assert.Equal(TarokConstants.TalonSize, deal.Talon.Count);
		for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
			Assert.Equal(TarokConstants.HandSize, deal.Hand(seat).Count);
		}
	}

	[Fact]
	public void A_thousand_deals_use_every_card_exactly_once() {
		for (int seed = 0; seed < 1000; seed++) {
			Deal deal = Deal.Create(seed);

			List<Card> all = [.. deal.Talon];
			for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
				all.AddRange(deal.Hand(seat));
			}

			Assert.Equal(TarokConstants.DeckSize, all.Count);
			Assert.Equal(TarokConstants.DeckSize, all.Distinct().Count());
		}
	}

	[Fact]
	public void The_same_seed_deals_the_same_hands() {
		Deal first = Deal.Create(seed: 2026);
		Deal second = Deal.Create(seed: 2026);

		Assert.Equal(first.Talon, second.Talon);
		for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
			Assert.Equal(first.Hand(seat), second.Hand(seat));
		}
	}

	[Fact]
	public void Different_seeds_deal_different_hands() {
		Assert.NotEqual(Deal.Create(seed: 1).Hand(0), Deal.Create(seed: 2).Hand(0));
	}

	[Fact]
	public void Hands_come_back_sorted_for_display() {
		Deal deal = Deal.Create(seed: 33);

		for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
			IReadOnlyList<Card> hand = deal.Hand(seat);
			Assert.Equal(hand.OrderBy(card => card), hand);
		}
	}

	[Fact]
	public void Cards_are_dealt_in_packets_of_six() {
		Card[] pack = Deck.Shuffled(seed: 8);
		Deal deal = Deal.FromOrderedPack(pack);

		Assert.Equal(pack.Take(6), deal.Talon);

		// Seat 0 gets pack[6..12] and pack[30..36]; the hand is sorted, so compare as sets.
		IEnumerable<Card> expectedSeat0 = pack.Skip(6).Take(6).Concat(pack.Skip(30).Take(6));
		Assert.Equal(expectedSeat0.OrderBy(card => card), deal.Hand(0));

		IEnumerable<Card> expectedSeat3 = pack.Skip(24).Take(6).Concat(pack.Skip(48).Take(6));
		Assert.Equal(expectedSeat3.OrderBy(card => card), deal.Hand(3));
	}

	[Fact]
	public void An_ordinary_deal_needs_no_redeal() {
		Deal deal = Deal.Create(seed: 5);

		Assert.False(deal.RequiresRedeal);
		Assert.Empty(deal.SeatsWithoutTrump);
	}

	[Fact]
	public void A_seat_dealt_no_trump_forces_a_redeal() {
		Deal deal = Deal.FromOrderedPack(PackWhereSeatZeroHoldsNoTrump());

		Assert.True(deal.RequiresRedeal);
		Assert.Single(deal.SeatsWithoutTrump);
		Assert.Equal(0, deal.SeatsWithoutTrump[0]);
		Assert.DoesNotContain(deal.Hand(0), card => card.IsTrump);
	}

	[Fact]
	public void A_pack_of_the_wrong_size_is_rejected() {
		Assert.Throws<ArgumentException>(() => Deal.FromOrderedPack(Deck.Full().Take(53).ToList()));
	}

	[Fact]
	public void A_pack_with_a_duplicate_is_rejected() {
		List<Card> pack = [.. Deck.Full().Take(53), Deck.Full()[0]];

		Assert.Throws<ArgumentException>(() => Deal.FromOrderedPack(pack));
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(4)]
	public void There_are_only_four_seats(int seat) {
		Deal deal = Deal.Create(seed: 1);

		Assert.Throws<ArgumentOutOfRangeException>(() => deal.Hand(seat));
	}

	/// <summary>
	/// 54 cards arranged so that seat 0 is dealt twelve plain-suit cards:
	/// talon, then packets of six in seat order, twice round.
	/// </summary>
	private static List<Card> PackWhereSeatZeroHoldsNoTrump() {
		List<Card> plain = Deck.Full().Where(card => !card.IsTrump).ToList();
		List<Card> trumps = Deck.Full().Where(card => card.IsTrump).ToList();

		List<Card> pack = [];
		pack.AddRange(trumps.Take(6));                 // talon
		pack.AddRange(plain.Take(6));                  // seat 0, first packet
		pack.AddRange(trumps.Skip(6).Take(6));         // seat 1
		pack.AddRange(trumps.Skip(12).Take(6));        // seat 2
		pack.AddRange(trumps.Skip(18).Take(4));        // seat 3, four trumps
		pack.AddRange(plain.Skip(6).Take(2));          // seat 3, two plain
		pack.AddRange(plain.Skip(8).Take(6));          // seat 0, second packet
		pack.AddRange(plain.Skip(14).Take(6));         // seat 1
		pack.AddRange(plain.Skip(20).Take(6));         // seat 2
		pack.AddRange(plain.Skip(26).Take(6));         // seat 3

		return pack;
	}
}
