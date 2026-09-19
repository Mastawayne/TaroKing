using TaroKing.Engine.Announcing;
using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;
using TaroKing.Engine.Play;
using TaroKing.Engine.Scoring;

namespace TaroKing.Engine.Session;

/// <summary>
/// The hand as one seat is allowed to see it. This is the only thing a client is ever sent:
/// your own cards, everybody else's card counts, and whatever is genuinely public at the table.
///
/// Two things stay hidden that a naive projection would leak. The partner is known to the engine
/// from the moment the king is called, but is only named here once that king has actually fallen
/// (or to the two players who already know). And the talon packets the declarer did not take go
/// to the opponents unseen, so nobody gets to look at them.
/// </summary>
public sealed record PlayerView {

	/// <summary>The seat this view belongs to, or -1 for a spectator who sits nowhere.</summary>
	public required int Seat { get; init; }

	/// <summary>A watcher rather than a player: sees the table, holds no cards, may do nothing.</summary>
	public bool IsSpectator { get; init; }

	public required GamePhase Phase { get; init; }

	public int? CurrentSeat { get; init; }

	public bool IsYourTurn => CurrentSeat == Seat;

	/// <summary>Your own cards. Never anybody else's.</summary>
	public IReadOnlyList<Card> Hand { get; init; } = [];

	/// <summary>How many cards each seat is holding, yours included.</summary>
	public IReadOnlyList<int> HandSizes { get; init; } = [];

	/// <summary>The odprti berač's hand once it is face up on the table, visible to everyone.</summary>
	public int? ExposedSeat { get; init; }

	public IReadOnlyList<Card> ExposedHand { get; init; } = [];

	public Contract? Contract { get; init; }

	public int? Declarer { get; init; }

	/// <summary>The suit that was called, which everybody hears.</summary>
	public Suit? CalledKingSuit { get; init; }

	/// <summary>The partner, but only once the table legitimately knows who it is.</summary>
	public int? KnownPartner { get; init; }

	/// <summary>True when you are the declarer or the holder of the called king.</summary>
	public bool YouAreDeclaringSide => Seat == Declarer || (KnownPartner is int partner && Seat == partner);

	/// <summary>
	/// Whether a seat is on your side, as far as you can tell. An unrevealed partner counts as an
	/// opponent, which is exactly the position a defender is in at the table.
	/// </summary>
	public bool IsOnYourSide(int seat) =>
		YouAreDeclaringSide
			? seat == Declarer || seat == KnownPartner
			: seat != Declarer && seat != KnownPartner;

	public IReadOnlyList<TrickCard> CurrentTrick { get; init; } = [];

	public IReadOnlyList<Trick> CompletedTricks { get; init; } = [];

	/// <summary>How many packets the talon is laid out in, before one is taken.</summary>
	public int TalonPacketCount { get; init; }

	/// <summary>The packet the declarer took, which is turned up for the table to see.</summary>
	public IReadOnlyList<Card> TakenTalonPacket { get; init; } = [];

	/// <summary>How many trumps the declarer laid away, which is public; the cards themselves are not.</summary>
	public int ShownDiscardCount { get; init; }

	/// <summary>How many cards the declarer has to lay away.</summary>
	public int DiscardCount { get; init; }

	public IReadOnlyList<AnnouncedBonus> Announcements { get; init; } = [];

	public int GameKontraMultiplier { get; init; } = 1;

	// --- what you may do right now ---

	public IReadOnlyList<Contract> LegalBids { get; init; } = [];

	public bool CanPassBid { get; init; }

	public IReadOnlyList<Suit> LegalKingCalls { get; init; } = [];

	public IReadOnlyList<Card> LegalDiscards { get; init; } = [];

	public bool AwaitsUpgradeDecision { get; init; }

	public IReadOnlyList<Bonus> LegalAnnouncements { get; init; } = [];

	public IReadOnlyList<KontraTarget> LegalKontras { get; init; } = [];

	public IReadOnlyList<Card> LegalPlays { get; init; } = [];

	public HandScore? Score { get; init; }

	/// <summary>Project a hand for one seat.</summary>
	public static PlayerView For(HandState hand, int seat) {
		ArgumentNullException.ThrowIfNull(hand);
		ArgumentOutOfRangeException.ThrowIfNegative(seat);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(seat, TarokConstants.PlayerCount);

		int? exposedSeat = ExposedSeatOf(hand);

		return new PlayerView {
			Seat = seat,
			Phase = hand.Phase,
			CurrentSeat = hand.CurrentSeat,
			Hand = CardsInHand(hand, seat),
			HandSizes = [.. Enumerable.Range(0, TarokConstants.PlayerCount).Select(other => CardsInHand(hand, other).Count)],
			ExposedSeat = exposedSeat,
			ExposedHand = exposedSeat is null ? [] : CardsInHand(hand, exposedSeat.Value),
			Contract = hand.PlayedContract,
			Declarer = hand.Declarer,
			CalledKingSuit = hand.KingCall?.Suit,
			KnownPartner = KnownPartnerFor(hand, seat),
			CurrentTrick = hand.Tricks?.CurrentTrick ?? [],
			CompletedTricks = hand.Tricks?.Tricks ?? [],
			TalonPacketCount = hand.Talon?.Packets.Count ?? 0,
			TakenTalonPacket = TakenPacket(hand),
			ShownDiscardCount = hand.Talon?.ShownDiscards.Count ?? 0,
			DiscardCount = hand.Talon?.Info.TalonCards ?? 0,
			Announcements = hand.Announcements?.Announcements ?? [],
			GameKontraMultiplier = hand.Announcements?.Multiplier(KontraTarget.Game) ?? 1,
			LegalBids = hand.Phase == GamePhase.Bidding && hand.CurrentSeat == seat ? hand.Bidding.LegalBids() : [],
			CanPassBid = hand.Phase == GamePhase.Bidding && hand.CurrentSeat == seat && hand.Bidding.CanPass(),
			LegalKingCalls = hand.CurrentSeat == seat ? hand.LegalKingCalls() : [],
			LegalDiscards = hand.Phase == GamePhase.Talon && hand.Declarer == seat ? hand.Talon!.LegalDiscards() : [],
			AwaitsUpgradeDecision = hand.AwaitsUpgradeDecision && hand.Declarer == seat,
			LegalAnnouncements = hand.Phase == GamePhase.Announcing && hand.CurrentSeat == seat
				? hand.Announcements!.LegalAnnouncements()
				: [],
			LegalKontras = hand.Phase == GamePhase.Announcing && hand.CurrentSeat == seat
				? hand.Announcements!.LegalKontras()
				: [],
			LegalPlays = hand.Phase == GamePhase.Play ? hand.Tricks!.LegalPlays(seat) : [],
			Score = hand.Score
		};
	}

	/// <summary>
	/// The hand as somebody watching over the table's shoulder sees it: every card that has been
	/// played, nobody's hand, and the partnership only once the table itself has worked it out.
	/// </summary>
	public static PlayerView ForSpectator(HandState hand) {
		ArgumentNullException.ThrowIfNull(hand);

		int? exposedSeat = ExposedSeatOf(hand);

		return new PlayerView {
			Seat = -1,
			IsSpectator = true,
			Phase = hand.Phase,
			CurrentSeat = hand.CurrentSeat,
			Hand = [],
			HandSizes = [.. Enumerable.Range(0, TarokConstants.PlayerCount).Select(other => CardsInHand(hand, other).Count)],
			ExposedSeat = exposedSeat,
			ExposedHand = exposedSeat is null ? [] : CardsInHand(hand, exposedSeat.Value),
			Contract = hand.PlayedContract,
			Declarer = hand.Declarer,
			CalledKingSuit = hand.KingCall?.Suit,
			KnownPartner = PublicPartner(hand),
			CurrentTrick = hand.Tricks?.CurrentTrick ?? [],
			CompletedTricks = hand.Tricks?.Tricks ?? [],
			TalonPacketCount = hand.Talon?.Packets.Count ?? 0,
			TakenTalonPacket = TakenPacket(hand),
			ShownDiscardCount = hand.Talon?.ShownDiscards.Count ?? 0,
			DiscardCount = hand.Talon?.Info.TalonCards ?? 0,
			Announcements = hand.Announcements?.Announcements ?? [],
			GameKontraMultiplier = hand.Announcements?.Multiplier(KontraTarget.Game) ?? 1,
			Score = hand.Score
		};
	}

	/// <summary>The partner as the whole table knows it — never earlier than the called king falls.</summary>
	private static int? PublicPartner(HandState hand) {
		if (hand.KingCall is null || hand.PartnerSeat is not int partner) {
			return null;
		}

		return hand.Phase == GamePhase.Finished || CalledKingHasFallen(hand) ? partner : null;
	}

	private static IReadOnlyList<Card> CardsInHand(HandState hand, int seat) {
		if (hand.Tricks is not null) {
			return hand.Tricks.Hand(seat);
		}

		// Before the cards are in play, the declarer's hand is the one the talon exchange left behind.
		if (hand.Talon is not null && hand.Declarer == seat && hand.Talon.ChosenPacket is not null) {
			return hand.Talon.Hand;
		}

		return hand.Deal.Hand(seat);
	}

	private static int? ExposedSeatOf(HandState hand) {
		if (hand.Tricks is null || hand.Declarer is not int declarer) {
			return null;
		}

		return hand.Tricks.IsHandExposed(declarer) ? declarer : null;
	}

	/// <summary>
	/// The partnership is public to the two who are in it, and to everybody once the called king
	/// has been played — or once the hand is over and there is nothing left to hide.
	/// </summary>
	private static int? KnownPartnerFor(HandState hand, int seat) {
		if (hand.KingCall is null || hand.PartnerSeat is not int partner) {
			return null;
		}

		if (seat == hand.Declarer || seat == partner || hand.Phase == GamePhase.Finished) {
			return partner;
		}

		return CalledKingHasFallen(hand) ? partner : null;
	}

	private static bool CalledKingHasFallen(HandState hand) {
		if (hand.Tricks is null || hand.KingCall is null) {
			return false;
		}

		Card king = hand.KingCall.King;

		foreach (Trick trick in hand.Tricks.Tricks) {
			if (trick.Cards.Any(played => played.Card == king)) {
				return true;
			}
		}

		return hand.Tricks.CurrentTrick.Any(played => played.Card == king);
	}

	private static IReadOnlyList<Card> TakenPacket(HandState hand) {
		if (hand.Talon?.ChosenPacket is not int packet) {
			return [];
		}

		return hand.Talon.Packets[packet];
	}
}
