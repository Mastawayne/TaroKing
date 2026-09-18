using TaroKing.Engine.Announcing;
using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;

namespace TaroKing.Engine.Session;

/// <summary>
/// One thing that happened in a hand. The log of these is the hand: given the seed and the events,
/// the engine rebuilds the identical state, which is what makes saving, reconnecting and reviewing
/// a finished game possible without storing any derived state at all.
/// </summary>
public abstract record GameEvent {

	/// <summary>The seat that acted, or null for events the table itself produced.</summary>
	public virtual int? Seat => null;
}

public sealed record HandDealt(int Seed, bool CompulsoryKlop) : GameEvent;

public sealed record BidPlaced(int Bidder, Contract Contract) : GameEvent {
	public override int? Seat => Bidder;
}

public sealed record BidPassed(int Bidder) : GameEvent {
	public override int? Seat => Bidder;
}

public sealed record KingCalled(int Declarer, Suit Suit) : GameEvent {
	public override int? Seat => Declarer;
}

public sealed record TalonPacketTaken(int Declarer, int Packet) : GameEvent {
	public override int? Seat => Declarer;
}

public sealed record CardsDiscarded(int Declarer, IReadOnlyList<Card> Cards) : GameEvent {
	public override int? Seat => Declarer;
}

/// <summary>A solo lifted to a barvni valat after the talon exchange.</summary>
public sealed record ContractUpgraded(int Declarer, Contract Contract) : GameEvent {
	public override int? Seat => Declarer;
}

/// <summary>A solo the declarer chose not to lift after all.</summary>
public sealed record ContractKept(int Declarer) : GameEvent {
	public override int? Seat => Declarer;
}

public sealed record BonusAnnounced(int Announcer, Bonus Bonus) : GameEvent {
	public override int? Seat => Announcer;
}

public sealed record KontraCalled(int Caller, KontraTarget Target) : GameEvent {
	public override int? Seat => Caller;
}

public sealed record AnnouncementPassed(int Announcer) : GameEvent {
	public override int? Seat => Announcer;
}

public sealed record CardPlayed(int Player, Card Card) : GameEvent {
	public override int? Seat => Player;
}
