using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;
using TaroKing.Engine.Randomness;
using TaroKing.Engine.Session;

namespace TaroKing.Engine.Tests.Session;

public class PlayerViewTests {

	[Theory]
	[InlineData(Contract.Two)]
	[InlineData(Contract.SoloOne)]
	[InlineData(Contract.Klop)]
	[InlineData(Contract.OpenBeggar)]
	public void No_view_ever_carries_another_players_cards(Contract contract) {
		for (int seed = 0; seed < 10; seed++) {
			HandState hand = HandStateTests.HandWith(contract, seed);
			Pcg32 random = new(seed);

			while (hand.Phase != GamePhase.Finished) {
				CheckEverySeatsView(hand);
				HandStateTests.Step(hand, random);
			}

			CheckEverySeatsView(hand);
		}
	}

	[Fact]
	public void Your_view_holds_your_own_hand_and_counts_for_the_rest() {
		HandState hand = HandStateTests.HandWith(Contract.Two);
		hand.CallKing(1, Suit.Clubs);

		PlayerView view = PlayerView.For(hand, 2);

		Assert.Equal(hand.Deal.Hand(2), view.Hand);
		Assert.Equal(TarokConstants.PlayerCount, view.HandSizes.Count);
		Assert.All(view.HandSizes, size => Assert.Equal(TarokConstants.HandSize, size));
	}

	[Fact]
	public void The_declarers_view_follows_the_talon_exchange() {
		HandState hand = HandStateTests.HandWith(Contract.Two);
		hand.CallKing(1, Suit.Clubs);
		hand.TakeTalonPacket(1, 0);

		PlayerView declarer = PlayerView.For(hand, 1);

		Assert.Equal(14, declarer.Hand.Count);
		Assert.Equal(2, declarer.TakenTalonPacket.Count);
		Assert.NotEmpty(declarer.LegalDiscards);

		// The other seats see the count change but get no cards.
		PlayerView opponent = PlayerView.For(hand, 2);
		Assert.Equal(14, opponent.HandSizes[1]);
		Assert.Empty(opponent.LegalDiscards);
	}

	[Fact]
	public void The_talon_the_declarer_left_behind_is_shown_to_nobody() {
		HandState hand = HandStateTests.HandWith(Contract.Two);
		hand.CallKing(1, Suit.Clubs);
		hand.TakeTalonPacket(1, 0);
		hand.Discard(1, hand.Talon!.LegalDiscards().Take(2));

		IReadOnlyList<Card> leftBehind = hand.Talon.OpponentTalon;

		for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
			PlayerView view = PlayerView.For(hand, seat);
			List<Card> visible = [.. view.Hand, .. view.ExposedHand, .. view.TakenTalonPacket];

			Assert.DoesNotContain(visible, card => leftBehind.Contains(card));
		}
	}

	[Fact]
	public void The_partner_stays_secret_until_the_called_king_falls() {
		HandState hand = FindHandWithAPartner(out int partner);
		int declarer = hand.Declarer!.Value;
		int outsider = Enumerable.Range(0, TarokConstants.PlayerCount).First(seat => seat != declarer && seat != partner);

		Assert.Equal(partner, PlayerView.For(hand, declarer).KnownPartner);
		Assert.Equal(partner, PlayerView.For(hand, partner).KnownPartner);
		Assert.Null(PlayerView.For(hand, outsider).KnownPartner);

		// Everybody hears which suit was called, though.
		Assert.NotNull(PlayerView.For(hand, outsider).CalledKingSuit);

		Card king = hand.KingCall!.King;
		Pcg32 random = new(7);

		while (hand.Phase != GamePhase.Finished && !KingHasBeenPlayed(hand, king)) {
			HandStateTests.Step(hand, random);
		}

		Assert.Equal(partner, PlayerView.For(hand, outsider).KnownPartner);
	}

	[Fact]
	public void Only_the_seat_on_turn_is_offered_moves() {
		HandState hand = HandStateTests.HandWith(Contract.Klop);

		int onTurn = hand.CurrentSeat!.Value;
		Assert.NotEmpty(PlayerView.For(hand, onTurn).LegalPlays);
		Assert.True(PlayerView.For(hand, onTurn).IsYourTurn);

		int waiting = (onTurn + 1) % TarokConstants.PlayerCount;
		Assert.Empty(PlayerView.For(hand, waiting).LegalPlays);
		Assert.False(PlayerView.For(hand, waiting).IsYourTurn);
	}

	[Fact]
	public void The_open_beggars_hand_is_shown_to_everyone_once_it_is_face_up() {
		HandState hand = HandStateTests.HandWith(Contract.OpenBeggar);
		int declarer = hand.Declarer!.Value;
		Pcg32 random = new(3);

		while (hand.Phase == GamePhase.Announcing) {
			HandStateTests.Step(hand, random);
		}

		Assert.All(Seats(), seat => Assert.Empty(PlayerView.For(hand, seat).ExposedHand));

		// Play out the first trick.
		while (hand.Tricks!.Tricks.Count == 0 && hand.Phase != GamePhase.Finished) {
			HandStateTests.Step(hand, random);
		}

		if (hand.Phase == GamePhase.Finished) {
			return; // the berač took the first trick and the hand was over at once
		}

		foreach (int seat in Seats()) {
			PlayerView view = PlayerView.For(hand, seat);
			Assert.Equal(declarer, view.ExposedSeat);
			Assert.Equal(hand.Tricks!.Hand(declarer), view.ExposedHand);
		}
	}

	[Fact]
	public void The_score_reaches_every_seat_once_the_hand_is_over() {
		HandState hand = HandStateTests.PlayFully(Contract.Two, seed: 5);

		foreach (int seat in Seats()) {
			PlayerView view = PlayerView.For(hand, seat);

			Assert.Equal(GamePhase.Finished, view.Phase);
			Assert.NotNull(view.Score);
			Assert.Empty(view.LegalPlays);
		}
	}

	[Fact]
	public void There_are_only_four_seats_to_look_from() {
		HandState hand = HandState.Create(seed: 1);

		Assert.Throws<ArgumentOutOfRangeException>(() => PlayerView.For(hand, 4));
		Assert.Throws<ArgumentOutOfRangeException>(() => PlayerView.For(hand, -1));
	}

	// --- helpers ---

	private static IEnumerable<int> Seats() => Enumerable.Range(0, TarokConstants.PlayerCount);

	private static void CheckEverySeatsView(HandState hand) {
		foreach (int seat in Seats()) {
			PlayerView view = PlayerView.For(hand, seat);
			IReadOnlyList<Card> own = CardsStillHeld(hand, seat);

			Assert.Equal(own, view.Hand);

			foreach (int other in Seats()) {
				if (other == seat || other == view.ExposedSeat) {
					continue;
				}

				IReadOnlyList<Card> theirs = CardsStillHeld(hand, other);
				Assert.DoesNotContain(view.Hand, card => theirs.Contains(card));
			}

			Assert.Equal(
				Seats().Select(other => CardsStillHeld(hand, other).Count),
				view.HandSizes);
		}
	}

	private static IReadOnlyList<Card> CardsStillHeld(HandState hand, int seat) {
		if (hand.Tricks is not null) {
			return hand.Tricks.Hand(seat);
		}

		if (hand.Talon is not null && hand.Declarer == seat && hand.Talon.ChosenPacket is not null) {
			return hand.Talon.Hand;
		}

		return hand.Deal.Hand(seat);
	}

	private static bool KingHasBeenPlayed(HandState hand, Card king) {
		if (hand.Tricks is null) {
			return false;
		}

		return hand.Tricks.Tricks.Any(trick => trick.Cards.Any(played => played.Card == king))
			|| hand.Tricks.CurrentTrick.Any(played => played.Card == king);
	}

	private static HandState FindHandWithAPartner(out int partner) {
		for (int seed = 0; seed < 200; seed++) {
			HandState hand = HandStateTests.HandWith(Contract.Two, seed);
			hand.CallKing(hand.Declarer!.Value, Suit.Clubs);

			if (hand.PartnerSeat is int found) {
				partner = found;
				return hand;
			}
		}

		throw new InvalidOperationException("No deal in 200 gave the declarer a partner.");
	}
}
