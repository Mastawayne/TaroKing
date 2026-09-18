using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;
using TaroKing.Engine.Dealing;
using TaroKing.Engine.Play;
using TaroKing.Engine.Randomness;

namespace TaroKing.Engine.Tests.Play;

public class TrickPlayTests {

	[Fact]
	public void A_hand_starts_with_the_lead_seat_on_turn() {
		TrickPlay play = Start(Contract.Three, seed: 1, leadSeat: 0);

		Assert.Equal(0, play.CurrentSeat);
		Assert.Equal(1, play.TrickNumber);
		Assert.Empty(play.CurrentTrick);
		Assert.False(play.IsComplete);
		Assert.Equal(TarokConstants.HandSize, play.Hand(0).Count);
	}

	[Fact]
	public void Four_cards_make_a_trick_and_the_winner_leads_the_next_one() {
		TrickPlay play = Start(Contract.Three, seed: 5, leadSeat: 0);

		for (int card = 0; card < TarokConstants.PlayerCount; card++) {
			play.Play(play.CurrentSeat, play.LegalPlays(play.CurrentSeat)[0]);
		}

		Assert.Single(play.Tricks);
		Trick trick = play.Tricks[0];

		Assert.Equal(1, trick.Number);
		Assert.Equal(0, trick.LeadSeat);
		Assert.Equal(TarokConstants.PlayerCount, trick.Cards.Count);
		Assert.Equal(trick.WinnerSeat, play.CurrentSeat);
		Assert.Equal(4, play.Won(trick.WinnerSeat).Count);
		Assert.Equal(1, play.TricksWonBy(trick.WinnerSeat));
		Assert.Equal(2, play.TrickNumber);
		Assert.Empty(play.CurrentTrick);
	}

	[Fact]
	public void Only_the_seat_on_turn_may_play() {
		TrickPlay play = Start(Contract.Three, seed: 2, leadSeat: 0);
		Card card = play.Hand(1)[0];

		Assert.Throws<InvalidOperationException>(() => play.Play(1, card));
		Assert.Empty(play.LegalPlays(1));
	}

	[Fact]
	public void You_cannot_play_a_card_you_do_not_hold() {
		TrickPlay play = Start(Contract.Three, seed: 3, leadSeat: 0);
		Card someoneElses = play.Hand(2)[0];

		Assert.Throws<InvalidOperationException>(() => play.Play(0, someoneElses));
	}

	[Fact]
	public void An_illegal_card_is_refused_even_when_it_is_in_hand() {
		Card lead = Card.Of(Suit.Hearts, SuitRank.Knight);
		Card follow = Card.Of(Suit.Hearts, SuitRank.King);
		Card trump = Card.Trump(20);

		IReadOnlyList<Card>[] hands = CraftedHands((0, lead), (1, follow), (1, trump));
		TrickPlay play = TrickPlay.Start(Contracts.Info(Contract.Three), hands, leadSeat: 0);

		play.Play(0, lead);

		// Seat 1 holds a heart, so the trump is not an option.
		Assert.Throws<InvalidOperationException>(() => play.Play(1, trump));
		Assert.Contains(follow, play.LegalPlays(1));

		play.Play(1, follow);
		Assert.Equal(2, play.CurrentTrick.Count);
	}

	[Fact]
	public void The_captured_mond_is_reported_when_it_happens() {
		// The pagat sits with the mond so it cannot steal the trick and muddy the test.
		IReadOnlyList<Card>[] hands = CraftedHands((0, Card.Mond), (0, Card.Pagat), (1, Card.Skis));
		TrickPlay play = TrickPlay.Start(Contracts.Info(Contract.Three), hands, leadSeat: 0);

		play.Play(0, Card.Mond);
		play.Play(1, Card.Skis);
		play.Play(2, play.LegalPlays(2)[0]);
		play.Play(3, play.LegalPlays(3)[0]);

		Assert.Equal(new[] { 0 }, play.CapturedMondSeats);
		Assert.Equal(1, play.Tricks[0].WinnerSeat);
	}

	[Fact]
	public void Klop_hands_a_talon_card_to_the_winner_of_each_of_the_first_six_tricks() {
		Deal deal = Deal.Create(seed: 77);
		TrickPlay play = TrickPlay.Start(
			Contracts.Info(Contract.Klop),
			[deal.Hand(0), deal.Hand(1), deal.Hand(2), deal.Hand(3)],
			leadSeat: 0,
			klopGifts: deal.Talon);

		PlayOut(play, seed: 77);

		List<Card> everything = [];
		for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
			everything.AddRange(play.Won(seat));
		}

		Assert.Equal(TarokConstants.DeckSize, everything.Count);
		Assert.Equal(TarokConstants.DeckSize, everything.Distinct().Count());
		Assert.Equal(TarokConstants.TotalCardPoints, CardScoring.Count(everything));
	}

	[Fact]
	public void The_gifts_stop_after_the_sixth_trick() {
		Deal deal = Deal.Create(seed: 91);
		TrickPlay play = TrickPlay.Start(
			Contracts.Info(Contract.Klop),
			[deal.Hand(0), deal.Hand(1), deal.Hand(2), deal.Hand(3)],
			leadSeat: 0,
			klopGifts: deal.Talon);

		int expected = 0;
		for (int trick = 1; trick <= TarokConstants.TrickCount; trick++) {
			PlayOneTrick(play);
			expected += 4 + (trick <= TarokConstants.TalonSize ? 1 : 0);

			int actual = Enumerable.Range(0, TarokConstants.PlayerCount).Sum(seat => play.Won(seat).Count);
			Assert.Equal(expected, actual);
		}
	}

	[Theory]
	[InlineData(Contract.Three)]
	[InlineData(Contract.SoloOne)]
	[InlineData(Contract.Klop)]
	[InlineData(Contract.Beggar)]
	[InlineData(Contract.OpenBeggar)]
	[InlineData(Contract.SoloWithout)]
	[InlineData(Contract.ColourValat)]
	[InlineData(Contract.Valat)]
	public void Any_contract_plays_out_to_twelve_tricks(Contract contract) {
		for (int seed = 0; seed < 50; seed++) {
			Deal deal = Deal.Create(seed);
			TrickPlay play = TrickPlay.Start(
				Contracts.Info(contract),
				[deal.Hand(0), deal.Hand(1), deal.Hand(2), deal.Hand(3)],
				leadSeat: seed % TarokConstants.PlayerCount);

			PlayOut(play, seed);

			Assert.True(play.IsComplete);
			Assert.Equal(TarokConstants.TrickCount, play.Tricks.Count);
			Assert.All(play.Tricks, trick => Assert.Equal(TarokConstants.PlayerCount, trick.Cards.Count));

			for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
				Assert.Empty(play.Hand(seat));
			}

			List<Card> played = [];
			for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
				played.AddRange(play.Won(seat));
			}

			Assert.Equal(48, played.Count);
			Assert.Equal(48, played.Distinct().Count());

			// The 48 cards played plus the six left in the talon are the whole pack.
			Assert.Equal(TarokConstants.TotalCardPoints, CardScoring.Count(played.Concat(deal.Talon)));
		}
	}

	[Fact]
	public void A_hand_of_the_wrong_shape_is_refused() {
		Deal deal = Deal.Create(seed: 1);
		IReadOnlyList<Card>[] good = [deal.Hand(0), deal.Hand(1), deal.Hand(2), deal.Hand(3)];
		ContractInfo info = Contracts.Info(Contract.Three);

		Assert.Throws<ArgumentException>(() => TrickPlay.Start(info, [good[0], good[1], good[2]], 0));
		Assert.Throws<ArgumentException>(() => TrickPlay.Start(info, [good[0], good[1], good[2], good[0]], 0));
		Assert.Throws<ArgumentOutOfRangeException>(() => TrickPlay.Start(info, good, 4));
	}

	[Fact]
	public void A_finished_hand_takes_no_more_cards() {
		TrickPlay play = Start(Contract.Three, seed: 9, leadSeat: 0);
		PlayOut(play, seed: 9);

		Assert.True(play.IsComplete);
		Assert.Empty(play.LegalPlays(play.CurrentSeat));
		Assert.Throws<InvalidOperationException>(() => play.Play(play.CurrentSeat, Card.Pagat));
	}

	// --- helpers ---

	private static TrickPlay Start(Contract contract, int seed, int leadSeat) {
		Deal deal = Deal.Create(seed);
		return TrickPlay.Start(
			Contracts.Info(contract),
			[deal.Hand(0), deal.Hand(1), deal.Hand(2), deal.Hand(3)],
			leadSeat);
	}

	private static void PlayOut(TrickPlay play, int seed) {
		Pcg32 random = new(seed);

		while (!play.IsComplete) {
			IReadOnlyList<Card> legal = play.LegalPlays(play.CurrentSeat);
			Assert.NotEmpty(legal);
			play.Play(play.CurrentSeat, legal[random.Next(legal.Count)]);
		}
	}

	private static void PlayOneTrick(TrickPlay play) {
		int target = play.Tricks.Count + 1;
		while (play.Tricks.Count < target) {
			play.Play(play.CurrentSeat, play.LegalPlays(play.CurrentSeat)[0]);
		}
	}

	/// <summary>
	/// Four hands of twelve built from the pack, with the named cards moved into the named seats.
	/// Everything not placed is filler, so a test only has to say what it actually cares about.
	/// </summary>
	private static IReadOnlyList<Card>[] CraftedHands(params (int Seat, Card Card)[] placements) {
		List<Card> pack = [.. Deck.Full()];
		List<Card>[] hands = new List<Card>[TarokConstants.PlayerCount];

		for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
			hands[seat] = [.. pack.Skip(seat * TarokConstants.HandSize).Take(TarokConstants.HandSize)];
		}

		// The last six cards stand in for the talon and can be swapped in and out freely.
		List<Card> spare = [.. pack.Skip(TarokConstants.PlayerCount * TarokConstants.HandSize)];
		HashSet<Card> placed = [];

		foreach ((int seat, Card card) in placements) {
			placed.Add(card);

			if (hands[seat].Contains(card)) {
				continue;
			}

			int giveIndex = hands[seat].FindIndex(held => !placed.Contains(held));
			Assert.True(giveIndex >= 0, "Nothing left in the hand to swap out.");
			Card give = hands[seat][giveIndex];

			int otherSeat = Array.FindIndex(hands, hand => hand.Contains(card));
			if (otherSeat >= 0) {
				hands[otherSeat][hands[otherSeat].IndexOf(card)] = give;
			} else {
				spare[spare.IndexOf(card)] = give;
			}

			hands[seat][giveIndex] = card;
		}

		return [.. hands];
	}
}
