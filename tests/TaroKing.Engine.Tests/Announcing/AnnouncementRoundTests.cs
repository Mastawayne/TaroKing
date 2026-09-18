using TaroKing.Engine.Announcing;
using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;

namespace TaroKing.Engine.Tests.Announcing;

public class AnnouncementRoundTests {

	private static readonly Card CalledKing = Card.Of(Suit.Hearts, SuitRank.King);

	// Seats 0 and 2 are the declaring side, seats 1 and 3 defend.
	private static readonly int[] Partnership = [Sides.Declaring, Sides.Defending, Sides.Declaring, Sides.Defending];

	[Fact]
	public void Bonus_values_match_the_scoring_table() {
		Assert.Equal((10, 20), Values(Bonus.Trula));
		Assert.Equal((10, 20), Values(Bonus.Kings));
		Assert.Equal((10, 20), Values(Bonus.KingUltimo));
		Assert.Equal((25, 50), Values(Bonus.PagatUltimo));
		Assert.Equal((250, 500), Values(Bonus.Valat));
	}

	[Fact]
	public void The_declarer_speaks_first() {
		AnnouncementRound round = Round(declarerSeat: 2);

		Assert.Equal(2, round.CurrentSeat);
		Assert.False(round.IsComplete);
	}

	[Fact]
	public void A_full_circle_of_passes_ends_the_round() {
		AnnouncementRound round = Round();

		round.Pass(0);
		round.Pass(1);
		round.Pass(2);
		Assert.False(round.IsComplete);

		round.Pass(3);
		Assert.True(round.IsComplete);
	}

	[Fact]
	public void An_announcement_keeps_the_turn_and_restarts_the_circle() {
		AnnouncementRound round = Round();

		round.Pass(0);
		round.Pass(1);
		round.Announce(2, Bonus.Trula);

		// Seat 2 may keep going, and the three passes before it no longer count.
		Assert.Equal(2, round.CurrentSeat);

		round.Pass(2);
		round.Pass(3);
		round.Pass(0);
		Assert.False(round.IsComplete);

		round.Pass(1);
		Assert.True(round.IsComplete);
	}

	[Fact]
	public void Several_announcements_can_be_made_in_one_turn() {
		AnnouncementRound round = Round();

		round.Announce(0, Bonus.Trula);
		round.Announce(0, Bonus.Kings);

		Assert.Equal(2, round.Announcements.Count);
		Assert.True(round.WasAnnounced(Bonus.Trula, Sides.Declaring));
		Assert.True(round.WasAnnounced(Bonus.Kings, Sides.Declaring));
	}

	[Fact]
	public void A_side_cannot_announce_the_same_bonus_twice() {
		AnnouncementRound round = Round();

		round.Announce(0, Bonus.Trula);
		round.Pass(0);
		round.Pass(1);

		// Seat 2 is the declarer's partner, so the trula is already claimed for that side.
		Assert.DoesNotContain(Bonus.Trula, round.LegalAnnouncements());
		Assert.Throws<InvalidOperationException>(() => round.Announce(2, Bonus.Trula));
	}

	[Fact]
	public void The_other_side_may_claim_the_same_bonus_for_itself() {
		AnnouncementRound round = Round();

		round.Announce(0, Bonus.Trula);
		round.Pass(0);

		Assert.Contains(Bonus.Trula, round.LegalAnnouncements());
		round.Announce(1, Bonus.Trula);

		Assert.True(round.WasAnnounced(Bonus.Trula, Sides.Defending));
	}

	[Fact]
	public void Only_the_pagat_holder_may_announce_pagat_ultimo() {
		AnnouncementRound round = Round();

		// Seat 1 holds the pagat in the crafted hands below.
		Assert.DoesNotContain(Bonus.PagatUltimo, round.LegalAnnouncements());
		Assert.Throws<InvalidOperationException>(() => round.Announce(0, Bonus.PagatUltimo));

		round.Pass(0);
		Assert.Contains(Bonus.PagatUltimo, round.LegalAnnouncements());
		round.Announce(1, Bonus.PagatUltimo);

		Assert.True(round.WasAnnounced(Bonus.PagatUltimo, Sides.Defending));
	}

	[Fact]
	public void Only_the_called_king_holder_may_announce_king_ultimo() {
		AnnouncementRound round = Round();

		Assert.DoesNotContain(Bonus.KingUltimo, round.LegalAnnouncements());

		round.Pass(0);
		round.Pass(1);

		// Seat 2 holds the called king.
		Assert.Contains(Bonus.KingUltimo, round.LegalAnnouncements());
		round.Announce(2, Bonus.KingUltimo);

		Assert.True(round.WasAnnounced(Bonus.KingUltimo, Sides.Declaring));
	}

	[Fact]
	public void Without_a_called_king_nobody_announces_king_ultimo() {
		AnnouncementRound round = Round(withCalledKing: false);

		round.Pass(0);
		round.Pass(1);

		Assert.DoesNotContain(Bonus.KingUltimo, round.LegalAnnouncements());
	}

	[Fact]
	public void Contracts_without_bonuses_allow_no_announcements() {
		AnnouncementRound round = Round(contract: Contract.SoloWithout);

		Assert.Empty(round.LegalAnnouncements());
		Assert.Throws<InvalidOperationException>(() => round.Announce(0, Bonus.Trula));
	}

	// --- kontra ---

	[Fact]
	public void The_game_starts_undoubled() {
		AnnouncementRound round = Round();

		Assert.Equal(0, round.KontraLevel(KontraTarget.Game));
		Assert.Equal(1, round.Multiplier(KontraTarget.Game));
	}

	[Fact]
	public void The_declaring_side_cannot_kontra_its_own_game() {
		AnnouncementRound round = Round();

		Assert.DoesNotContain(KontraTarget.Game, round.LegalKontras());
		Assert.Throws<InvalidOperationException>(() => round.Kontra(0, KontraTarget.Game));
	}

	[Fact]
	public void Kontra_rekontra_subkontra_mordkontra_double_in_turn() {
		AnnouncementRound round = Round();
		round.Pass(0);

		// Defender kontras.
		Assert.Contains(KontraTarget.Game, round.LegalKontras());
		round.Kontra(1, KontraTarget.Game);
		Assert.Equal(2, round.Multiplier(KontraTarget.Game));

		// The same side cannot double again.
		Assert.DoesNotContain(KontraTarget.Game, round.LegalKontras());
		round.Pass(1);

		// Declaring side rekontras.
		round.Kontra(2, KontraTarget.Game);
		Assert.Equal(4, round.Multiplier(KontraTarget.Game));
		round.Pass(2);

		// Defenders subkontra.
		round.Kontra(3, KontraTarget.Game);
		Assert.Equal(8, round.Multiplier(KontraTarget.Game));
		round.Pass(3);

		// Declaring side mordkontras, and that is the ceiling.
		round.Kontra(0, KontraTarget.Game);
		Assert.Equal(16, round.Multiplier(KontraTarget.Game));
		Assert.Equal(AnnouncementRound.MaxKontraLevel, round.KontraLevel(KontraTarget.Game));

		round.Pass(0);
		Assert.DoesNotContain(KontraTarget.Game, round.LegalKontras());
	}

	[Fact]
	public void An_announcement_can_be_doubled_on_its_own() {
		AnnouncementRound round = Round();
		round.Announce(0, Bonus.Trula);
		round.Pass(0);

		KontraTarget trula = new(Bonus.Trula, Sides.Declaring);

		Assert.Contains(trula, round.LegalKontras());
		round.Kontra(1, trula);

		Assert.Equal(2, round.Multiplier(trula));
		Assert.Equal(1, round.Multiplier(KontraTarget.Game));
	}

	[Fact]
	public void You_cannot_kontra_your_own_partners_announcement() {
		AnnouncementRound round = Round();
		round.Announce(0, Bonus.Trula);
		round.Pass(0);
		round.Pass(1);

		KontraTarget trula = new(Bonus.Trula, Sides.Declaring);

		// Seat 2 is on the announcing side.
		Assert.DoesNotContain(trula, round.LegalKontras());
		Assert.Throws<InvalidOperationException>(() => round.Kontra(2, trula));
	}

	[Fact]
	public void A_bonus_nobody_announced_cannot_be_doubled() {
		AnnouncementRound round = Round();
		round.Pass(0);

		Assert.Throws<InvalidOperationException>(
			() => round.Kontra(1, new KontraTarget(Bonus.PagatUltimo, Sides.Declaring)));
	}

	[Fact]
	public void Klop_is_not_kontra_able_unless_the_table_plays_it_that_way() {
		AnnouncementRound plain = Round(contract: Contract.Klop);
		plain.Pass(0);
		Assert.Empty(plain.LegalKontras());

		AnnouncementRound house = Round(contract: Contract.Klop, allowKlopKontra: true);
		house.Pass(0);
		Assert.Contains(KontraTarget.Game, house.LegalKontras());
	}

	[Fact]
	public void Speaking_out_of_turn_is_refused() {
		AnnouncementRound round = Round();

		Assert.Throws<InvalidOperationException>(() => round.Pass(1));
		Assert.Throws<InvalidOperationException>(() => round.Announce(1, Bonus.Trula));
	}

	[Fact]
	public void A_finished_round_takes_nothing_more() {
		AnnouncementRound round = Round();
		round.Pass(0);
		round.Pass(1);
		round.Pass(2);
		round.Pass(3);

		Assert.True(round.IsComplete);
		Assert.Empty(round.LegalAnnouncements());
		Assert.Empty(round.LegalKontras());
		Assert.Throws<InvalidOperationException>(() => round.Pass(0));
	}

	// --- helpers ---

	private static (int Silent, int Announced) Values(Bonus bonus) {
		BonusInfo info = Bonuses.Info(bonus);
		return (info.SilentValue, info.AnnouncedValue);
	}

	/// <summary>
	/// A round where seat 1 holds the pagat and seat 2 holds the called king, so the holder-only
	/// rules have something to bite on.
	/// </summary>
	private static AnnouncementRound Round(
		Contract contract = Contract.Two,
		int declarerSeat = 0,
		bool withCalledKing = true,
		bool allowKlopKontra = false) {

		IReadOnlyList<Card>[] hands = [
			[Card.Mond, Card.Of(Suit.Clubs, SuitRank.King)],
			[Card.Pagat, Card.Of(Suit.Spades, SuitRank.Queen)],
			[CalledKing, Card.Trump(9)],
			[Card.Skis, Card.Of(Suit.Diamonds, SuitRank.Jack)]
		];

		return AnnouncementRound.Start(
			Contracts.Info(contract),
			Partnership,
			declarerSeat,
			hands,
			withCalledKing ? CalledKing : null,
			allowKlopKontra);
	}
}
