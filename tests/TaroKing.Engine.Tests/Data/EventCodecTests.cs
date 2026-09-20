using TaroKing.App.Services;
using TaroKing.Data;
using TaroKing.Engine.Announcing;
using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;
using TaroKing.Engine.Session;

namespace TaroKing.Engine.Tests.Data;

/// <summary>
/// The stored form of a hand has to give the same hand back. Record equality will not do for the
/// comparison — a discard event carries a list — so a round trip is judged the way it matters:
/// re-encoded text is identical, and a replay of the decoded log lands on the same score.
/// </summary>
public class EventCodecTests {

	[Fact]
	public async Task Every_event_of_a_played_session_survives_the_round_trip() {
		using LocalGame game = new("codec", new SessionOptions { HandCount = 6, Seed = 2024, AutoPlayHuman = true, BotDelayMs = 0 });
		await game.PlayOutAsync();

		foreach (HandRecord record in game.Records) {
			IReadOnlyList<EncodedEvent> stored = EventCodec.EncodeAll(record.Events);
			IReadOnlyList<GameEvent> decoded = EventCodec.DecodeAll(stored);

			Assert.Equal(record.Events.Count, decoded.Count);
			Assert.Equal(stored, EventCodec.EncodeAll(decoded));

			HandState replayed = HandState.Replay(decoded);

			Assert.Equal(GamePhase.Finished, replayed.Phase);
			Assert.Equal(record.Scores, replayed.Score!.BySeat);
		}
	}

	[Fact]
	public void Every_kind_of_event_has_a_shape() {
		GameEvent[] all = [
			new HandDealt(-17, true),
			new RadlcApplied(2),
			new BidPlaced(1, Contract.SoloTwo),
			new BidPassed(2),
			new KingCalled(0, Suit.Diamonds),
			new TalonPacketTaken(0, 2),
			new CardsDiscarded(0, [Card.Of(Suit.Clubs, SuitRank.Queen), Card.Trump(7)]),
			new ContractUpgraded(0, Contract.ColourValat),
			new ContractKept(0),
			new BonusAnnounced(3, Bonus.PagatUltimo),
			new KontraCalled(1, KontraTarget.Game),
			new KontraCalled(2, new KontraTarget(Bonus.Trula, Sides.Defending)),
			new AnnouncementPassed(3),
			new CardPlayed(2, Card.Skis)
		];

		foreach (GameEvent original in all) {
			EncodedEvent stored = EventCodec.Encode(original);
			GameEvent back = EventCodec.Decode(stored);

			Assert.Equal(original.GetType(), back.GetType());
			Assert.Equal(original.Seat, back.Seat);
			Assert.Equal(stored, EventCodec.Encode(back));
		}
	}

	[Fact]
	public void A_discard_keeps_its_cards_in_order() {
		CardsDiscarded original = new(0, [Card.Of(Suit.Hearts, SuitRank.Pip1), Card.Trump(21), Card.Of(Suit.Spades, SuitRank.Jack)]);

		CardsDiscarded back = Assert.IsType<CardsDiscarded>(EventCodec.Decode(EventCodec.Encode(original)));

		Assert.Equal(original.Cards, back.Cards);
	}

	[Theory]
	[InlineData("T22")]
	[InlineData("T1")]
	[InlineData("H8")]
	[InlineData("C1")]
	public void A_card_code_reads_back_as_the_same_card(string code) {
		Card card = EventCodec.ParseCard(code);

		Assert.Equal(code, EventCodec.CardCode(card));
	}

	[Fact]
	public void The_skis_and_the_pagat_are_where_they_should_be() {
		Assert.Equal(Card.Skis, EventCodec.ParseCard("T22"));
		Assert.Equal(Card.Pagat, EventCodec.ParseCard("T1"));
		Assert.Equal(Card.Of(Suit.Hearts, SuitRank.King), EventCodec.ParseCard("H8"));
	}

	[Fact]
	public void Rubbish_is_refused() {
		Assert.Throws<FormatException>(() => EventCodec.ParseCard("X3"));
		Assert.Throws<FormatException>(() => EventCodec.ParseCard("T"));
		Assert.Throws<ArgumentException>(() => EventCodec.Decode(new EncodedEvent("Nonsense", 0, "")));
	}
}
