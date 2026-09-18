using TaroKing.Engine.Announcing;
using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;
using TaroKing.Engine.Randomness;
using TaroKing.Engine.Session;

namespace TaroKing.Engine.Tests.Session;

public class HandStateTests {

	[Fact]
	public void A_hand_opens_with_the_auction_and_a_dealt_pack() {
		HandState hand = HandState.Create(seed: 1);

		Assert.Equal(GamePhase.Bidding, hand.Phase);
		Assert.Equal(1, hand.CurrentSeat);
		Assert.Null(hand.PlayedContract);
		Assert.Null(hand.Declarer);
		Assert.Single(hand.Events);
		Assert.Equal(TarokConstants.HandSize, hand.Deal.Hand(0).Count);
	}

	[Fact]
	public void A_called_king_contract_walks_through_every_phase_in_order() {
		HandState hand = HandWith(Contract.Two);

		Assert.Equal(GamePhase.KingCall, hand.Phase);
		Assert.Equal(1, hand.Declarer);

		hand.CallKing(1, Suit.Hearts);
		Assert.Equal(GamePhase.Talon, hand.Phase);
		Assert.Equal(3, hand.Talon!.Packets.Count);

		hand.TakeTalonPacket(1, 0);
		hand.Discard(1, hand.Talon.LegalDiscards().Take(2));

		Assert.Equal(GamePhase.Announcing, hand.Phase);
		Assert.Equal(1, hand.CurrentSeat);

		PassAllAnnouncements(hand);

		Assert.Equal(GamePhase.Play, hand.Phase);
		Assert.Equal(0, hand.CurrentSeat);  // forehand leads a dva
	}

	[Fact]
	public void Klop_goes_straight_from_the_deal_to_the_cards() {
		HandState hand = HandWith(Contract.Klop);

		Assert.Equal(GamePhase.Play, hand.Phase);
		Assert.Empty(hand.Talon!.Packets);
		Assert.Null(hand.Announcements);
	}

	[Fact]
	public void A_beggar_skips_the_talon_but_still_has_an_announcement_round() {
		HandState hand = HandWith(Contract.Beggar);

		Assert.Equal(GamePhase.Announcing, hand.Phase);
		Assert.Equal(TarokConstants.TalonSize, hand.Talon!.OpponentTalon.Count);
		Assert.Empty(hand.Announcements!.LegalAnnouncements());
	}

	[Fact]
	public void A_solo_is_asked_whether_the_talon_made_a_colour_valat_of_it() {
		HandState hand = HandWith(Contract.SoloTwo);

		hand.TakeTalonPacket(1, 0);
		hand.Discard(1, hand.Talon!.LegalDiscards().Take(2));

		Assert.True(hand.AwaitsUpgradeDecision);
		Assert.Equal(GamePhase.Talon, hand.Phase);

		hand.KeepContract(1);

		Assert.False(hand.AwaitsUpgradeDecision);
		Assert.Equal(Contract.SoloTwo, hand.PlayedContract);
		Assert.Equal(GamePhase.Announcing, hand.Phase);
	}

	[Fact]
	public void Lifting_a_solo_changes_the_contract_that_is_played() {
		HandState hand = HandWith(Contract.SoloTwo);
		hand.TakeTalonPacket(1, 0);
		hand.Discard(1, hand.Talon!.LegalDiscards().Take(2));

		hand.UpgradeToColourValat(1);

		Assert.Equal(Contract.ColourValat, hand.PlayedContract);
		Assert.Equal(Contract.SoloTwo, hand.Bidding.FinalContract);
		Assert.True(hand.Info!.TrumpsArePlainSuit);
	}

	[Fact]
	public void A_numbered_contract_is_never_offered_the_upgrade() {
		HandState hand = HandWith(Contract.Two);
		hand.CallKing(1, Suit.Hearts);
		hand.TakeTalonPacket(1, 0);
		hand.Discard(1, hand.Talon!.LegalDiscards().Take(2));

		Assert.False(hand.AwaitsUpgradeDecision);
		Assert.Throws<InvalidOperationException>(() => hand.UpgradeToColourValat(1));
	}

	[Fact]
	public void Actions_belonging_to_another_phase_are_refused() {
		HandState hand = HandState.Create(seed: 1);

		Assert.Throws<InvalidOperationException>(() => hand.CallKing(1, Suit.Hearts));
		Assert.Throws<InvalidOperationException>(() => hand.TakeTalonPacket(1, 0));
		Assert.Throws<InvalidOperationException>(() => hand.PlayCard(1, hand.Deal.Hand(1)[0]));
	}

	[Fact]
	public void A_compulsory_klop_needs_no_auction_at_all() {
		HandState hand = HandState.Create(seed: 4, compulsoryKlop: true);

		Assert.True(hand.IsCompulsoryKlop);
		Assert.Equal(Contract.Klop, hand.PlayedContract);
		Assert.Equal(BiddingState.ForehandSeat, hand.Declarer);
		Assert.Equal(GamePhase.Play, hand.Phase);
	}

	[Theory]
	[InlineData(Contract.Klop)]
	[InlineData(Contract.Two)]
	[InlineData(Contract.SoloOne)]
	[InlineData(Contract.Beggar)]
	[InlineData(Contract.SoloWithout)]
	[InlineData(Contract.OpenBeggar)]
	[InlineData(Contract.ColourValat)]
	[InlineData(Contract.Valat)]
	public void Any_contract_plays_through_to_a_score(Contract contract) {
		for (int seed = 0; seed < 20; seed++) {
			HandState hand = PlayFully(contract, seed);

			Assert.Equal(GamePhase.Finished, hand.Phase);
			Assert.NotNull(hand.Score);
			Assert.Null(hand.CurrentSeat);
			Assert.Equal(TarokConstants.PlayerCount, hand.Score!.BySeat.Count);
		}
	}

	[Theory]
	[InlineData(Contract.Klop)]
	[InlineData(Contract.Two)]
	[InlineData(Contract.SoloOne)]
	[InlineData(Contract.Beggar)]
	[InlineData(Contract.Valat)]
	public void A_hand_replayed_from_its_events_comes_out_identical(Contract contract) {
		for (int seed = 0; seed < 20; seed++) {
			HandState original = PlayFully(contract, seed);
			HandState replayed = HandState.Replay(original.Events);

			Assert.Equal(original.Phase, replayed.Phase);
			Assert.Equal(original.PlayedContract, replayed.PlayedContract);
			Assert.Equal(original.Declarer, replayed.Declarer);
			Assert.Equal(original.PartnerSeat, replayed.PartnerSeat);
			Assert.Equal(original.Events.Count, replayed.Events.Count);
			Assert.Equal(original.Score!.BySeat, replayed.Score!.BySeat);
			Assert.Equal(original.Tricks!.Tricks.Count, replayed.Tricks!.Tricks.Count);

			for (int trick = 0; trick < original.Tricks.Tricks.Count; trick++) {
				Assert.Equal(original.Tricks.Tricks[trick].WinnerSeat, replayed.Tricks.Tricks[trick].WinnerSeat);
				Assert.Equal(original.Tricks.Tricks[trick].Cards, replayed.Tricks.Tricks[trick].Cards);
			}
		}
	}

	[Fact]
	public void A_partial_hand_can_be_replayed_too() {
		HandState hand = HandWith(Contract.Two);
		hand.CallKing(1, Suit.Diamonds);
		hand.TakeTalonPacket(1, 1);

		HandState replayed = HandState.Replay(hand.Events);

		Assert.Equal(GamePhase.Talon, replayed.Phase);
		Assert.Equal(1, replayed.Talon!.ChosenPacket);
		Assert.Equal(hand.PartnerSeat, replayed.PartnerSeat);
	}

	[Fact]
	public void An_event_log_has_to_start_with_the_deal() {
		Assert.Throws<ArgumentException>(() => HandState.Replay([]));
		Assert.Throws<ArgumentException>(() => HandState.Replay([new BidPassed(1)]));
	}

	[Fact]
	public void The_log_records_every_action_in_order() {
		HandState hand = HandWith(Contract.Klop);

		Assert.IsType<HandDealt>(hand.Events[0]);
		Assert.IsType<BidPassed>(hand.Events[1]);
		Assert.IsType<BidPlaced>(hand.Events[4]);
		Assert.Equal(0, hand.Events[4].Seat);
	}

	// --- helpers ---

	/// <summary>An auction that lands on a given contract: seat 1 bids it, or forehand takes it when all pass.</summary>
	internal static HandState HandWith(Contract contract, int seed = 1) {
		HandState hand = HandState.Create(seed);

		if (Contracts.Info(contract).ForehandOnly) {
			hand.PassBid(1);
			hand.PassBid(2);
			hand.PassBid(3);
			hand.PlaceBid(BiddingState.ForehandSeat, contract);
			return hand;
		}

		hand.PlaceBid(1, contract);
		hand.PassBid(2);
		hand.PassBid(3);
		hand.PassBid(BiddingState.ForehandSeat);
		return hand;
	}

	internal static HandState PlayFully(Contract contract, int seed) {
		HandState hand = HandWith(contract, seed);
		Pcg32 random = new(seed);

		while (hand.Phase != GamePhase.Finished) {
			Step(hand, random);
		}

		return hand;
	}

	internal static void Step(HandState hand, Pcg32 random) {
		int seat = hand.CurrentSeat!.Value;

		switch (hand.Phase) {
			case GamePhase.KingCall:
				hand.CallKing(seat, hand.LegalKingCalls()[0]);
				break;

			case GamePhase.Talon when hand.Talon!.NeedsPacketChoice:
				hand.TakeTalonPacket(seat, random.Next(hand.Talon.Packets.Count));
				break;

			case GamePhase.Talon when hand.Talon!.NeedsDiscard:
				hand.Discard(seat, hand.Talon.LegalDiscards().Take(hand.Talon.Info.TalonCards));
				break;

			case GamePhase.Talon when hand.AwaitsUpgradeDecision:
				hand.KeepContract(seat);
				break;

			case GamePhase.Announcing:
				hand.PassAnnouncement(seat);
				break;

			case GamePhase.Play:
				IReadOnlyList<Card> legal = hand.Tricks!.LegalPlays(seat);
				hand.PlayCard(seat, legal[random.Next(legal.Count)]);
				break;

			default:
				throw new InvalidOperationException($"Nothing to do in {hand.Phase}.");
		}
	}

	private static void PassAllAnnouncements(HandState hand) {
		while (hand.Phase == GamePhase.Announcing) {
			hand.PassAnnouncement(hand.CurrentSeat!.Value);
		}
	}
}
