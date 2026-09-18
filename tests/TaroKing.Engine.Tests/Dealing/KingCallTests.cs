using TaroKing.Engine.Cards;
using TaroKing.Engine.Dealing;

namespace TaroKing.Engine.Tests.Dealing;

public class KingCallTests {

	private static readonly Card CalledKing = Card.Of(Suit.Hearts, SuitRank.King);

	[Fact]
	public void The_seat_holding_the_king_becomes_the_partner() {
		KingCall call = KingCall.Resolve(Suit.Hearts, declarerSeat: 1, HandsWithKingAt(2), Talon());

		Assert.Equal(2, call.PartnerSeat);
		Assert.False(call.DeclarerIsAlone);
		Assert.False(call.KingIsInTalon);
		Assert.False(call.DeclarerHoldsCalledKing);
		Assert.Equal(CalledKing, call.King);
	}

	[Fact]
	public void Calling_your_own_king_means_playing_alone() {
		KingCall call = KingCall.Resolve(Suit.Hearts, declarerSeat: 1, HandsWithKingAt(1), Talon());

		Assert.True(call.DeclarerHoldsCalledKing);
		Assert.True(call.DeclarerIsAlone);
		Assert.Null(call.PartnerSeat);
	}

	[Fact]
	public void A_king_lying_in_the_talon_also_means_playing_alone() {
		IReadOnlyList<Card> talon = [CalledKing, Card.Trump(2), Card.Trump(3), Card.Trump(4), Card.Trump(5), Card.Trump(6)];
		KingCall call = KingCall.Resolve(Suit.Hearts, declarerSeat: 1, HandsWithoutTheKing(), talon);

		Assert.True(call.KingIsInTalon);
		Assert.True(call.DeclarerIsAlone);
		Assert.Null(call.PartnerSeat);
		Assert.False(call.DeclarerHoldsCalledKing);
	}

	[Fact]
	public void A_king_is_called_in_a_plain_suit() {
		Assert.Throws<ArgumentOutOfRangeException>(
			() => KingCall.Resolve(Suit.Trump, declarerSeat: 0, HandsWithKingAt(2), Talon()));
	}

	[Fact]
	public void The_called_king_has_to_be_somewhere() {
		Assert.Throws<ArgumentException>(
			() => KingCall.Resolve(Suit.Hearts, declarerSeat: 0, HandsWithoutTheKing(), Talon()));
	}

	[Fact]
	public void Every_suit_can_be_called() {
		foreach (Suit suit in new[] { Suit.Clubs, Suit.Spades, Suit.Hearts, Suit.Diamonds }) {
			Card king = Card.Of(suit, SuitRank.King);
			List<Card>[] hands = [[], [], [king], []];

			KingCall call = KingCall.Resolve(suit, declarerSeat: 0, hands, Talon());

			Assert.Equal(king, call.King);
			Assert.Equal(2, call.PartnerSeat);
		}
	}

	[Fact]
	public void A_real_deal_always_places_every_king() {
		for (int seed = 0; seed < 200; seed++) {
			Deal deal = Deal.Create(seed);
			IReadOnlyList<Card>[] hands = [deal.Hand(0), deal.Hand(1), deal.Hand(2), deal.Hand(3)];

			foreach (Suit suit in new[] { Suit.Clubs, Suit.Spades, Suit.Hearts, Suit.Diamonds }) {
				KingCall call = KingCall.Resolve(suit, declarerSeat: 0, hands, deal.Talon);

				bool accountedFor = call.PartnerSeat is not null || call.KingIsInTalon || call.DeclarerHoldsCalledKing;
				Assert.True(accountedFor, $"Seed {seed}: the {suit} king went missing.");
				Assert.NotEqual(0, call.PartnerSeat ?? -1);
			}
		}
	}

	private static List<Card>[] HandsWithKingAt(int seat) {
		List<Card>[] hands = [[], [], [], []];
		hands[seat].Add(CalledKing);
		return hands;
	}

	private static List<Card>[] HandsWithoutTheKing() => [
		[Card.Trump(10)],
		[Card.Of(Suit.Hearts, SuitRank.Queen)],
		[Card.Of(Suit.Clubs, SuitRank.King)],
		[Card.Pagat]
	];

	private static IReadOnlyList<Card> Talon() => [
		Card.Trump(2), Card.Trump(3), Card.Trump(4), Card.Trump(5), Card.Trump(6), Card.Trump(7)
	];
}
