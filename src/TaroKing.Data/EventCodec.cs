using System.Globalization;

using TaroKing.Engine.Announcing;
using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;
using TaroKing.Engine.Session;

namespace TaroKing.Data;

/// <summary>
/// One stored event: what kind, who did it, and its data as a short string.
/// </summary>
public readonly record struct EncodedEvent(string Kind, int? Seat, string Payload);

/// <summary>
/// Writes the engine's events down and reads them back. Explicit on purpose: every kind is named
/// here, cards are two or three characters, and nothing depends on a serializer's opinion of a
/// record hierarchy. If the engine grows an event, this file refuses to compile until it is added.
///
/// Cards: "T21" is trump XXI, "H8" is the king of hearts; the suit letter is C, S, H, D or T and the
/// number is the engine's rank.
/// </summary>
public static class EventCodec {

	public static EncodedEvent Encode(GameEvent gameEvent) {
		ArgumentNullException.ThrowIfNull(gameEvent);

		return gameEvent switch {
			HandDealt dealt => new("HandDealt", null, $"{N(dealt.Seed)}|{(dealt.CompulsoryKlop ? 1 : 0)}"),
			RadlcApplied radlc => new("RadlcApplied", null, N(radlc.Multiplier)),
			BidPlaced bid => new("BidPlaced", bid.Bidder, N((int)bid.Contract)),
			BidPassed passed => new("BidPassed", passed.Bidder, ""),
			KingCalled king => new("KingCalled", king.Declarer, SuitLetter(king.Suit).ToString()),
			TalonPacketTaken packet => new("TalonPacketTaken", packet.Declarer, N(packet.Packet)),
			CardsDiscarded discarded => new("CardsDiscarded", discarded.Declarer, string.Join(',', discarded.Cards.Select(CardCode))),
			ContractUpgraded upgraded => new("ContractUpgraded", upgraded.Declarer, N((int)upgraded.Contract)),
			ContractKept kept => new("ContractKept", kept.Declarer, ""),
			BonusAnnounced bonus => new("BonusAnnounced", bonus.Announcer, N((int)bonus.Bonus)),
			KontraCalled kontra => new("KontraCalled", kontra.Caller, $"{(kontra.Target.Kind is Bonus kind ? N((int)kind) : "-")}|{N(kontra.Target.Side)}"),
			AnnouncementPassed passed => new("AnnouncementPassed", passed.Announcer, ""),
			CardPlayed played => new("CardPlayed", played.Player, CardCode(played.Card)),
			_ => throw new ArgumentException($"No encoding for {gameEvent.GetType().Name}.", nameof(gameEvent))
		};
	}

	public static GameEvent Decode(EncodedEvent stored) {
		string[] parts = stored.Payload.Split('|');
		int seat = stored.Seat ?? -1;

		return stored.Kind switch {
			"HandDealt" => new HandDealt(I(parts[0]), parts[1] == "1"),
			"RadlcApplied" => new RadlcApplied(I(parts[0])),
			"BidPlaced" => new BidPlaced(seat, (Contract)I(parts[0])),
			"BidPassed" => new BidPassed(seat),
			"KingCalled" => new KingCalled(seat, SuitOf(parts[0][0])),
			"TalonPacketTaken" => new TalonPacketTaken(seat, I(parts[0])),
			"CardsDiscarded" => new CardsDiscarded(seat, [.. parts[0].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(ParseCard)]),
			"ContractUpgraded" => new ContractUpgraded(seat, (Contract)I(parts[0])),
			"ContractKept" => new ContractKept(seat),
			"BonusAnnounced" => new BonusAnnounced(seat, (Bonus)I(parts[0])),
			"KontraCalled" => new KontraCalled(seat, new KontraTarget(parts[0] == "-" ? null : (Bonus?)(Bonus)I(parts[0]), I(parts[1]))),
			"AnnouncementPassed" => new AnnouncementPassed(seat),
			"CardPlayed" => new CardPlayed(seat, ParseCard(parts[0])),
			_ => throw new ArgumentException($"Unknown event kind '{stored.Kind}'.", nameof(stored))
		};
	}

	public static IReadOnlyList<EncodedEvent> EncodeAll(IEnumerable<GameEvent> events) =>
		[.. events.Select(Encode)];

	public static IReadOnlyList<GameEvent> DecodeAll(IEnumerable<EncodedEvent> stored) =>
		[.. stored.Select(Decode)];

	// --- cards ---

	public static string CardCode(Card card) => $"{SuitLetter(card.Suit)}{N(card.Rank)}";

	public static Card ParseCard(string code) {
		if (string.IsNullOrEmpty(code) || code.Length < 2) {
			throw new FormatException($"'{code}' is not a card.");
		}

		return new Card(SuitOf(code[0]), I(code[1..]));
	}

	private static char SuitLetter(Suit suit) => suit switch {
		Suit.Clubs => 'C',
		Suit.Spades => 'S',
		Suit.Hearts => 'H',
		Suit.Diamonds => 'D',
		Suit.Trump => 'T',
		_ => throw new ArgumentOutOfRangeException(nameof(suit), suit, "Unknown suit.")
	};

	private static Suit SuitOf(char letter) => letter switch {
		'C' => Suit.Clubs,
		'S' => Suit.Spades,
		'H' => Suit.Hearts,
		'D' => Suit.Diamonds,
		'T' => Suit.Trump,
		_ => throw new FormatException($"'{letter}' is not a suit.")
	};

	private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

	private static int I(string text) => int.Parse(text, CultureInfo.InvariantCulture);
}
