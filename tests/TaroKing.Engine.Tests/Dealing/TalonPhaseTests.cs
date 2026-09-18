using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;
using TaroKing.Engine.Dealing;

namespace TaroKing.Engine.Tests.Dealing;

public class TalonPhaseTests {

	/// <summary>Twelve cards holding every five-pointer there is, plus five low cards to lay away.</summary>
	private static readonly Card[] Hand = [
		Card.Of(Suit.Clubs, SuitRank.King),
		Card.Of(Suit.Spades, SuitRank.King),
		Card.Of(Suit.Hearts, SuitRank.King),
		Card.Of(Suit.Diamonds, SuitRank.King),
		Card.Skis,
		Card.Mond,
		Card.Pagat,
		Card.Of(Suit.Clubs, SuitRank.Pip1),
		Card.Of(Suit.Clubs, SuitRank.Pip2),
		Card.Of(Suit.Clubs, SuitRank.Pip3),
		Card.Of(Suit.Clubs, SuitRank.Pip4),
		Card.Of(Suit.Spades, SuitRank.Pip1)
	];

	private static readonly Card[] Talon = [
		Card.Trump(2), Card.Trump(3), Card.Trump(4), Card.Trump(5), Card.Trump(6), Card.Trump(7)
	];

	[Theory]
	[InlineData(Contract.Three, 2, 3)]
	[InlineData(Contract.Two, 3, 2)]
	[InlineData(Contract.One, 6, 1)]
	[InlineData(Contract.SoloThree, 2, 3)]
	[InlineData(Contract.SoloTwo, 3, 2)]
	[InlineData(Contract.SoloOne, 6, 1)]
	public void The_talon_is_laid_out_in_equal_packets(Contract contract, int packets, int perPacket) {
		TalonPhase phase = TalonPhase.Create(contract, Talon, Hand);

		Assert.Equal(packets, phase.Packets.Count);
		Assert.All(phase.Packets, packet => Assert.Equal(perPacket, packet.Count));
		Assert.Equal(Talon, phase.Packets.SelectMany(packet => packet));
		Assert.True(phase.NeedsPacketChoice);
		Assert.False(phase.IsComplete);
	}

	[Theory]
	[InlineData(Contract.Beggar)]
	[InlineData(Contract.SoloWithout)]
	[InlineData(Contract.OpenBeggar)]
	[InlineData(Contract.ColourValat)]
	[InlineData(Contract.Valat)]
	public void Contracts_without_a_talon_hand_it_straight_to_the_opponents(Contract contract) {
		TalonPhase phase = TalonPhase.Create(contract, Talon, Hand);

		Assert.Empty(phase.Packets);
		Assert.Equal(Talon, phase.OpponentTalon);
		Assert.Empty(phase.KlopGifts);
		Assert.True(phase.IsComplete);
		Assert.Equal(TarokConstants.HandSize, phase.Hand.Count);
		Assert.Throws<InvalidOperationException>(() => phase.ChoosePacket(0));
	}

	[Fact]
	public void Klop_keeps_the_talon_as_gifts_for_the_first_six_tricks() {
		TalonPhase phase = TalonPhase.Create(Contract.Klop, Talon, Hand);

		Assert.Equal(Talon, phase.KlopGifts);
		Assert.Empty(phase.Packets);
		Assert.Empty(phase.OpponentTalon);
		Assert.True(phase.IsComplete);
	}

	[Fact]
	public void Taking_a_packet_leaves_the_rest_to_the_opponents() {
		TalonPhase phase = TalonPhase.Create(Contract.Three, Talon, Hand);
		phase.ChoosePacket(0);

		Assert.Equal(0, phase.ChosenPacket);
		Assert.Equal(15, phase.Hand.Count);
		Assert.Contains(Card.Trump(2), phase.Hand);
		Assert.Equal(3, phase.OpponentTalon.Count);
		Assert.Equal(new[] { Card.Trump(5), Card.Trump(6), Card.Trump(7) }, phase.OpponentTalon);
		Assert.True(phase.NeedsDiscard);
		Assert.False(phase.IsComplete);
	}

	[Fact]
	public void The_second_packet_can_be_taken_just_as_well() {
		TalonPhase phase = TalonPhase.Create(Contract.Three, Talon, Hand);
		phase.ChoosePacket(1);

		Assert.Equal(new[] { Card.Trump(2), Card.Trump(3), Card.Trump(4) }, phase.OpponentTalon);
		Assert.Contains(Card.Trump(7), phase.Hand);
	}

	[Fact]
	public void Five_pointers_are_not_on_the_list_of_legal_lay_aways() {
		TalonPhase phase = TalonPhase.Create(Contract.Three, Talon, Hand);
		phase.ChoosePacket(0);

		IReadOnlyList<Card> legal = phase.LegalDiscards();

		// 15 cards less the four kings and the trula.
		Assert.Equal(8, legal.Count);
		Assert.DoesNotContain(legal, card => card.IsFivePointer);
		Assert.Contains(Card.Trump(2), legal);
		Assert.Contains(Card.Of(Suit.Clubs, SuitRank.Pip1), legal);
	}

	[Fact]
	public void Laying_away_brings_the_hand_back_to_twelve() {
		TalonPhase phase = TalonPhase.Create(Contract.Three, Talon, Hand);
		phase.ChoosePacket(0);
		phase.Discard([Card.Trump(2), Card.Of(Suit.Clubs, SuitRank.Pip1), Card.Of(Suit.Spades, SuitRank.Pip1)]);

		Assert.True(phase.IsComplete);
		Assert.Equal(TarokConstants.HandSize, phase.Hand.Count);
		Assert.Equal(3, phase.Discards.Count);
		Assert.DoesNotContain(Card.Trump(2), phase.Hand);
	}

	[Fact]
	public void Laid_away_trumps_are_shown_and_other_cards_are_not() {
		TalonPhase phase = TalonPhase.Create(Contract.Two, Talon, Hand);
		phase.ChoosePacket(0);
		phase.Discard([Card.Trump(2), Card.Of(Suit.Clubs, SuitRank.Pip1)]);

		Assert.Equal(new[] { Card.Trump(2) }, phase.ShownDiscards);
	}

	[Fact]
	public void A_five_pointer_may_never_be_laid_away() {
		TalonPhase phase = TalonPhase.Create(Contract.One, Talon, Hand);
		phase.ChoosePacket(0);

		Assert.Throws<ArgumentException>(() => phase.Discard([Card.Of(Suit.Hearts, SuitRank.King)]));
		Assert.Throws<ArgumentException>(() => phase.Discard([Card.Pagat]));
		Assert.Throws<ArgumentException>(() => phase.Discard([Card.Mond]));
		Assert.Throws<ArgumentException>(() => phase.Discard([Card.Skis]));
		Assert.False(phase.IsComplete);
	}

	[Fact]
	public void The_lay_away_has_to_match_the_number_of_cards_taken() {
		TalonPhase phase = TalonPhase.Create(Contract.Three, Talon, Hand);
		phase.ChoosePacket(0);

		Assert.Throws<ArgumentException>(() => phase.Discard([Card.Trump(2)]));
		Assert.Throws<ArgumentException>(() => phase.Discard([Card.Trump(2), Card.Trump(3)]));
	}

	[Fact]
	public void Only_cards_actually_held_can_be_laid_away() {
		TalonPhase phase = TalonPhase.Create(Contract.One, Talon, Hand);
		phase.ChoosePacket(0);

		Assert.Throws<ArgumentException>(() => phase.Discard([Card.Trump(20)]));
	}

	[Fact]
	public void The_same_card_cannot_be_laid_away_twice() {
		TalonPhase phase = TalonPhase.Create(Contract.Two, Talon, Hand);
		phase.ChoosePacket(0);

		Assert.Throws<ArgumentException>(() => phase.Discard([Card.Trump(2), Card.Trump(2)]));
	}

	[Fact]
	public void The_order_is_packet_first_then_lay_away() {
		TalonPhase phase = TalonPhase.Create(Contract.Three, Talon, Hand);

		Assert.Empty(phase.LegalDiscards());
		Assert.Throws<InvalidOperationException>(
			() => phase.Discard([Card.Trump(2), Card.Trump(3), Card.Trump(4)]));

		phase.ChoosePacket(0);

		Assert.Throws<InvalidOperationException>(() => phase.ChoosePacket(1));
	}

	[Fact]
	public void There_is_no_third_packet_in_a_three() {
		TalonPhase phase = TalonPhase.Create(Contract.Three, Talon, Hand);

		Assert.Throws<ArgumentOutOfRangeException>(() => phase.ChoosePacket(2));
		Assert.Throws<ArgumentOutOfRangeException>(() => phase.ChoosePacket(-1));
	}

	[Fact]
	public void The_talon_and_the_hand_must_be_a_real_deal() {
		Assert.Throws<ArgumentException>(() => TalonPhase.Create(Contract.Three, Talon[..5], Hand));
		Assert.Throws<ArgumentException>(() => TalonPhase.Create(Contract.Three, Talon, Hand[..11]));

		Card[] overlapping = [Hand[0], .. Talon[1..]];
		Assert.Throws<ArgumentException>(() => TalonPhase.Create(Contract.Three, overlapping, Hand));
	}

	[Fact]
	public void A_dealt_hand_goes_through_the_exchange_without_losing_a_card() {
		for (int seed = 0; seed < 100; seed++) {
			Deal deal = Deal.Create(seed);
			TalonPhase phase = TalonPhase.Create(Contract.Two, deal.Talon, deal.Hand(0));

			phase.ChoosePacket(seed % 3);
			phase.Discard(phase.LegalDiscards().Take(2));

			Assert.Equal(TarokConstants.HandSize, phase.Hand.Count);
			Assert.Equal(2, phase.Discards.Count);
			Assert.Equal(4, phase.OpponentTalon.Count);

			List<Card> everything = [.. phase.Hand, .. phase.Discards, .. phase.OpponentTalon];
			Assert.Equal(18, everything.Count);
			Assert.Equal(18, everything.Distinct().Count());
		}
	}
}
