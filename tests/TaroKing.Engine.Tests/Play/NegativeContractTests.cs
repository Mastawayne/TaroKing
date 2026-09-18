using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;
using TaroKing.Engine.Dealing;
using TaroKing.Engine.Play;
using TaroKing.Engine.Randomness;

namespace TaroKing.Engine.Tests.Play;

public class NegativeContractTests {

	// --- a berač that takes a trick is over on the spot ---

	[Fact]
	public void A_beggar_that_takes_a_trick_is_decided_immediately() {
		TrickPlay play = BeggarWithSkisInHand();

		play.Play(0, Card.Skis);
		PlayRestOfTrick(play);

		Assert.Equal(0, play.Tricks[0].WinnerSeat);
		Assert.Equal(HandEnding.NegativeContractBroken, play.Ending);
		Assert.True(play.IsComplete);
		Assert.True(play.EndedEarly);
		Assert.Single(play.Tricks);

		// Nothing more is played: the remaining cards stay in hand.
		Assert.Equal(11, play.Hand(0).Count);
		Assert.Empty(play.LegalPlays(play.CurrentSeat));
		Assert.Throws<InvalidOperationException>(() => play.Play(play.CurrentSeat, play.Hand(play.CurrentSeat)[0]));
	}

	[Fact]
	public void Without_a_context_the_engine_has_no_sides_and_plays_on() {
		TrickPlay play = BeggarWithSkisInHand(withContext: false);

		play.Play(0, Card.Skis);
		PlayRestOfTrick(play);

		Assert.Equal(0, play.Tricks[0].WinnerSeat);
		Assert.Equal(HandEnding.InProgress, play.Ending);
		Assert.False(play.IsComplete);
	}

	[Theory]
	[InlineData(Contract.Beggar)]
	[InlineData(Contract.OpenBeggar)]
	public void A_beggar_either_takes_nothing_or_ends_the_moment_it_takes_something(Contract contract) {
		for (int seed = 0; seed < 50; seed++) {
			TrickPlay play = PlayOutDeal(contract, seed, declarerSeat: 0);

			Assert.True(play.IsComplete);

			if (play.Ending == HandEnding.AllTricksPlayed) {
				Assert.Equal(0, play.TricksWonBy(0));
				Assert.Equal(TarokConstants.TrickCount, play.Tricks.Count);
			} else {
				Assert.Equal(HandEnding.NegativeContractBroken, play.Ending);
				Assert.Equal(1, play.TricksWonBy(0));
				Assert.Equal(0, play.Tricks[^1].WinnerSeat);
			}
		}
	}

	// --- a valat that drops a trick is over on the spot ---

	[Theory]
	[InlineData(Contract.Valat)]
	[InlineData(Contract.ColourValat)]
	public void A_valat_either_takes_everything_or_ends_the_moment_it_drops_a_trick(Contract contract) {
		for (int seed = 0; seed < 50; seed++) {
			TrickPlay play = PlayOutDeal(contract, seed, declarerSeat: 0);

			Assert.True(play.IsComplete);

			if (play.Ending == HandEnding.AllTricksPlayed) {
				Assert.Equal(TarokConstants.TrickCount, play.TricksWonBy(0));
			} else {
				Assert.Equal(HandEnding.ValatBroken, play.Ending);
				Assert.NotEqual(0, play.Tricks[^1].WinnerSeat);
			}
		}
	}

	[Fact]
	public void A_partner_taking_a_trick_does_not_break_a_valat() {
		// Contrived, but it proves the check looks at sides rather than at the declarer alone.
		Deal deal = Deal.Create(seed: 12);
		TrickPlay play = TrickPlay.Start(
			Contracts.Info(Contract.Valat),
			[deal.Hand(0), deal.Hand(1), deal.Hand(2), deal.Hand(3)],
			leadSeat: 0,
			context: new PlayContext { DeclarerSeat = 0, PartnerSeat = 1 });

		while (!play.IsComplete) {
			play.Play(play.CurrentSeat, play.LegalPlays(play.CurrentSeat)[0]);
		}

		if (play.Ending == HandEnding.ValatBroken) {
			int winner = play.Tricks[^1].WinnerSeat;
			Assert.True(winner is 2 or 3, $"Seat {winner} is on the declaring side and should not break the valat.");
		}
	}

	// --- klop keeps going ---

	[Fact]
	public void Klop_is_negative_but_never_ends_early() {
		Deal deal = Deal.Create(seed: 21);
		TrickPlay play = TrickPlay.Start(
			Contracts.Info(Contract.Klop),
			[deal.Hand(0), deal.Hand(1), deal.Hand(2), deal.Hand(3)],
			leadSeat: 0,
			klopGifts: deal.Talon,
			context: new PlayContext { DeclarerSeat = 0 });

		PlayOut(play, seed: 21);

		Assert.Equal(HandEnding.AllTricksPlayed, play.Ending);
		Assert.Equal(TarokConstants.TrickCount, play.Tricks.Count);
	}

	// --- the open berač plays face up ---

	[Fact]
	public void The_open_beggar_turns_its_hand_up_after_the_first_trick() {
		TrickPlay play = OpenBeggarLosingTheFirstTrick();

		Assert.False(play.IsHandExposed(0));

		PlayOneTrick(play);

		Assert.NotEqual(0, play.Tricks[0].WinnerSeat);
		Assert.True(play.IsHandExposed(0));
		Assert.False(play.IsHandExposed(1));
		Assert.False(play.IsHandExposed(2));
		Assert.False(play.IsHandExposed(3));
	}

	[Fact]
	public void A_plain_beggar_never_shows_its_hand() {
		TrickPlay play = BeggarWithSkisInHand();
		PlayOneTrick(play);

		Assert.False(play.IsHandExposed(0));
	}

	// --- upgrading to a colour valat ---

	[Theory]
	[InlineData(Contract.SoloThree, true)]
	[InlineData(Contract.SoloTwo, true)]
	[InlineData(Contract.SoloOne, true)]
	[InlineData(Contract.Three, false)]
	[InlineData(Contract.Two, false)]
	[InlineData(Contract.One, false)]
	[InlineData(Contract.Klop, false)]
	[InlineData(Contract.Beggar, false)]
	[InlineData(Contract.SoloWithout, false)]
	[InlineData(Contract.ColourValat, false)]
	[InlineData(Contract.Valat, false)]
	public void Only_a_solo_can_be_lifted_to_a_colour_valat(Contract contract, bool expected) {
		Assert.Equal(expected, Contracts.CanUpgradeToColourValat(contract));
	}

	// --- helpers ---

	private static TrickPlay BeggarWithSkisInHand(bool withContext = true) {
		IReadOnlyList<Card>[] hands = CraftedHands((0, Card.Skis));

		return TrickPlay.Start(
			Contracts.Info(Contract.Beggar),
			hands,
			leadSeat: 0,
			context: withContext ? new PlayContext { DeclarerSeat = 0 } : null);
	}

	private static TrickPlay OpenBeggarLosingTheFirstTrick() {
		// Seat 0 leads the lowest club; the seats holding trumps have to take it.
		IReadOnlyList<Card>[] hands = CraftedHands((0, Card.Of(Suit.Clubs, SuitRank.Pip1)));

		return TrickPlay.Start(
			Contracts.Info(Contract.OpenBeggar),
			hands,
			leadSeat: 0,
			context: new PlayContext { DeclarerSeat = 0 });
	}

	private static TrickPlay PlayOutDeal(Contract contract, int seed, int declarerSeat) {
		Deal deal = Deal.Create(seed);
		TrickPlay play = TrickPlay.Start(
			Contracts.Info(contract),
			[deal.Hand(0), deal.Hand(1), deal.Hand(2), deal.Hand(3)],
			leadSeat: declarerSeat,
			context: new PlayContext { DeclarerSeat = declarerSeat });

		PlayOut(play, seed);
		return play;
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
		while (play.Tricks.Count < target && !play.IsComplete) {
			play.Play(play.CurrentSeat, play.LegalPlays(play.CurrentSeat)[0]);
		}
	}

	private static void PlayRestOfTrick(TrickPlay play) {
		while (play.CurrentTrick.Count > 0 && !play.IsComplete) {
			play.Play(play.CurrentSeat, play.LegalPlays(play.CurrentSeat)[0]);
		}
	}

	/// <summary>Four hands of twelve from the pack, with the named cards moved into the named seats.</summary>
	private static IReadOnlyList<Card>[] CraftedHands(params (int Seat, Card Card)[] placements) {
		List<Card> pack = [.. Deck.Full()];
		List<Card>[] hands = new List<Card>[TarokConstants.PlayerCount];

		for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
			hands[seat] = [.. pack.Skip(seat * TarokConstants.HandSize).Take(TarokConstants.HandSize)];
		}

		List<Card> spare = [.. pack.Skip(TarokConstants.PlayerCount * TarokConstants.HandSize)];
		HashSet<Card> placed = [];

		foreach ((int seat, Card card) in placements) {
			placed.Add(card);

			if (hands[seat].Contains(card)) {
				continue;
			}

			int giveIndex = hands[seat].FindIndex(held => !placed.Contains(held));
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
