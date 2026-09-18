using TaroKing.Engine.Bidding;
using TaroKing.Engine.Randomness;

namespace TaroKing.Engine.Tests.Bidding;

public class BiddingStateTests {

	[Fact]
	public void Forehand_stays_silent_so_the_auction_opens_with_seat_one() {
		BiddingState state = BiddingState.Start();

		Assert.Equal(1, state.CurrentSeat);
		Assert.False(state.IsComplete);
		Assert.False(state.IsForehandPrivilege);
	}

	[Fact]
	public void The_lowest_bid_for_anyone_but_forehand_is_two() {
		BiddingState state = BiddingState.Start();

		Assert.Equal(Contract.Two, state.LegalBids()[0]);
		Assert.DoesNotContain(Contract.Klop, state.LegalBids());
		Assert.DoesNotContain(Contract.Three, state.LegalBids());
		Assert.Throws<InvalidOperationException>(() => state.Place(1, Contract.Three));
	}

	[Fact]
	public void When_the_other_three_pass_forehand_may_name_anything() {
		BiddingState state = PassTo(BiddingState.Start(), 1, 2, 3);

		Assert.True(state.IsForehandPrivilege);
		Assert.Equal(BiddingState.ForehandSeat, state.CurrentSeat);
		Assert.Equal(12, state.LegalBids().Count);
		Assert.Contains(Contract.Klop, state.LegalBids());
		Assert.Contains(Contract.Three, state.LegalBids());
	}

	[Fact]
	public void Forehand_may_not_pass_on_the_privilege() {
		BiddingState state = PassTo(BiddingState.Start(), 1, 2, 3);

		Assert.False(state.CanPass());
		Assert.Throws<InvalidOperationException>(() => state.Pass(BiddingState.ForehandSeat));
	}

	[Theory]
	[InlineData(Contract.Klop)]
	[InlineData(Contract.Three)]
	[InlineData(Contract.SoloOne)]
	[InlineData(Contract.Valat)]
	public void Forehands_choice_ends_the_auction(Contract chosen) {
		BiddingState state = PassTo(BiddingState.Start(), 1, 2, 3);
		state.Place(BiddingState.ForehandSeat, chosen);

		Assert.True(state.IsComplete);
		Assert.Equal(BiddingState.ForehandSeat, state.Declarer);
		Assert.Equal(chosen, state.FinalContract);
	}

	[Fact]
	public void A_junior_player_has_to_raise() {
		BiddingState state = BiddingState.Start();
		state.Place(1, Contract.Two);

		// Seat 2 is junior to seat 1 and cannot simply match the bid.
		Assert.Equal(2, state.CurrentSeat);
		Assert.Equal(Contract.One, state.LegalBids()[0]);
		Assert.Throws<InvalidOperationException>(() => state.Place(2, Contract.Two));
	}

	[Fact]
	public void A_senior_player_may_match() {
		BiddingState state = BiddingState.Start();
		state.Place(1, Contract.Two);
		state.Pass(2);
		state.Pass(3);

		// Forehand outranks seat 1, so holding the same contract is enough.
		Assert.Equal(BiddingState.ForehandSeat, state.CurrentSeat);
		Assert.Equal(Contract.Two, state.LegalBids()[0]);

		state.Place(BiddingState.ForehandSeat, Contract.Two);

		// Holding does not end the auction: seat 1 is still in and may raise.
		Assert.False(state.IsComplete);
		Assert.Equal(1, state.CurrentSeat);

		state.Pass(1);

		Assert.True(state.IsComplete);
		Assert.Equal(BiddingState.ForehandSeat, state.Declarer);
		Assert.Equal(Contract.Two, state.FinalContract);
	}

	[Fact]
	public void Holding_hands_the_bid_back_to_the_original_bidder_if_forehand_then_passes() {
		BiddingState state = BiddingState.Start();
		state.Place(1, Contract.Two);
		state.Pass(2);
		state.Pass(3);
		state.Pass(BiddingState.ForehandSeat);

		Assert.True(state.IsComplete);
		Assert.Equal(1, state.Declarer);
		Assert.Equal(Contract.Two, state.FinalContract);
	}

	[Fact]
	public void A_bidder_who_was_outbid_by_a_senior_must_go_higher_to_get_it_back() {
		BiddingState state = BiddingState.Start();
		state.Place(1, Contract.Two);
		state.Pass(2);
		state.Pass(3);
		state.Place(BiddingState.ForehandSeat, Contract.Two);

		Assert.False(state.IsComplete);
		Assert.Equal(1, state.CurrentSeat);
		Assert.Equal(Contract.One, state.LegalBids()[0]);

		state.Place(1, Contract.One);
		state.Pass(BiddingState.ForehandSeat);

		Assert.True(state.IsComplete);
		Assert.Equal(1, state.Declarer);
		Assert.Equal(Contract.One, state.FinalContract);
	}

	[Fact]
	public void Nobody_can_overcall_a_valat_except_a_senior_holding_it() {
		BiddingState state = BiddingState.Start();
		state.Place(1, Contract.Valat);

		Assert.Empty(state.LegalBids());
		Assert.True(state.CanPass());

		state.Pass(2);
		state.Pass(3);

		Assert.Equal(new[] { Contract.Valat }, state.LegalBids());
	}

	[Fact]
	public void A_player_who_passed_is_out_of_the_auction() {
		BiddingState state = BiddingState.Start();
		state.Pass(1);
		state.Place(2, Contract.Two);

		Assert.True(state.HasPassed(1));
		Assert.Equal(3, state.CurrentSeat);
		Assert.Throws<InvalidOperationException>(() => state.Place(1, Contract.SoloOne));
	}

	[Fact]
	public void Bidding_out_of_turn_is_refused() {
		BiddingState state = BiddingState.Start();

		Assert.Throws<InvalidOperationException>(() => state.Place(3, Contract.Two));
		Assert.Throws<InvalidOperationException>(() => state.Pass(BiddingState.ForehandSeat));
	}

	[Fact]
	public void A_finished_auction_takes_no_more_bids() {
		BiddingState state = PassTo(BiddingState.Start(), 1, 2, 3);
		state.Place(BiddingState.ForehandSeat, Contract.Klop);

		Assert.Throws<InvalidOperationException>(() => state.Pass(1));
		Assert.Empty(state.LegalBids());
		Assert.False(state.CanPass());
	}

	[Fact]
	public void The_declarer_is_unknown_until_the_auction_ends() {
		BiddingState state = BiddingState.Start();

		Assert.Throws<InvalidOperationException>(() => _ = state.Declarer);
		Assert.Throws<InvalidOperationException>(() => _ = state.FinalContract);
	}

	[Fact]
	public void The_history_records_every_call_in_order() {
		BiddingState state = BiddingState.Start();
		state.Place(1, Contract.Two);
		state.Pass(2);

		Assert.Equal(2, state.History.Count);
		Assert.Equal(new BidEntry(1, Contract.Two), state.History[0]);
		Assert.True(state.History[1].IsPass);
		Assert.Equal(2, state.History[1].Seat);
	}

	[Fact]
	public void A_compulsory_klop_is_a_finished_auction_owned_by_forehand() {
		BiddingState state = BiddingState.CompulsoryKlop();

		Assert.True(state.IsComplete);
		Assert.True(state.IsCompulsoryKlop);
		Assert.Equal(Contract.Klop, state.FinalContract);
		Assert.Equal(BiddingState.ForehandSeat, state.Declarer);
		Assert.Empty(state.History);
	}

	[Fact]
	public void Every_random_auction_ends_with_a_valid_declarer() {
		for (int seed = 0; seed < 500; seed++) {
			Pcg32 random = new(seed);
			BiddingState state = BiddingState.Start();

			int calls = 0;
			while (!state.IsComplete) {
				Assert.True(++calls < 40, $"Auction {seed} did not finish.");

				IReadOnlyList<Contract> legal = state.LegalBids();
				if (legal.Count == 0 || (state.CanPass() && random.Next(3) == 0)) {
					Assert.True(state.CanPass(), $"Auction {seed} left a seat with nothing legal to do.");
					state.Pass(state.CurrentSeat);
				} else {
					state.Place(state.CurrentSeat, legal[random.Next(legal.Count)]);
				}
			}

			Assert.InRange(state.Declarer, 0, TarokConstants.PlayerCount - 1);

			if (Contracts.Info(state.FinalContract).ForehandOnly) {
				Assert.Equal(BiddingState.ForehandSeat, state.Declarer);
			}
		}
	}

	private static BiddingState PassTo(BiddingState state, params int[] seats) {
		foreach (int seat in seats) {
			state.Pass(seat);
		}

		return state;
	}
}
