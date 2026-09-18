using TaroKing.Engine.Announcing;
using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;
using TaroKing.Engine.Dealing;
using TaroKing.Engine.Play;
using TaroKing.Engine.Scoring;

namespace TaroKing.Engine.Session;

/// <summary>Where a hand has got to.</summary>
public enum GamePhase {
	Bidding = 0,
	KingCall = 1,
	Talon = 2,
	Announcing = 3,
	Play = 4,
	Finished = 5
}

/// <summary>
/// One hand of tarok from the deal to the score, driving the pieces built in the earlier phases
/// and recording every action as it goes. Nothing here decides a rule — it only knows what comes
/// after what, and refuses anything out of order or out of turn.
/// </summary>
public sealed class HandState {

	private readonly List<GameEvent> _events = [];

	private Contract? _upgrade;
	private bool _upgradeDecided;

	private HandState(int seed, bool compulsoryKlop) {
		Seed = seed;
		IsCompulsoryKlop = compulsoryKlop;
		Deal = Dealing.Deal.Create(seed);
		Bidding = compulsoryKlop ? BiddingState.CompulsoryKlop() : BiddingState.Start();

		_events.Add(new HandDealt(seed, compulsoryKlop));

		if (Bidding.IsComplete) {
			LeaveBidding();
		}
	}

	public int Seed { get; }

	public bool IsCompulsoryKlop { get; }

	public Deal Deal { get; }

	public BiddingState Bidding { get; }

	public KingCall? KingCall { get; private set; }

	public TalonPhase? Talon { get; private set; }

	public AnnouncementRound? Announcements { get; private set; }

	public TrickPlay? Tricks { get; private set; }

	public HandScore? Score { get; private set; }

	public GamePhase Phase { get; private set; } = GamePhase.Bidding;

	public IReadOnlyList<GameEvent> Events => _events;

	/// <summary>The contract being played, once the auction is over — including a later upgrade.</summary>
	public Contract? PlayedContract => Bidding.IsComplete ? _upgrade ?? Bidding.FinalContract : null;

	public ContractInfo? Info => PlayedContract is null ? null : Contracts.Info(PlayedContract.Value);

	public int? Declarer => Bidding.IsComplete ? Bidding.Declarer : null;

	/// <summary>The declarer's partner, known to the engine from the moment the king is called.</summary>
	public int? PartnerSeat => KingCall?.PartnerSeat;

	/// <summary>Whose turn it is, whatever the hand is currently doing.</summary>
	public int? CurrentSeat => Phase switch {
		GamePhase.Bidding => Bidding.CurrentSeat,
		GamePhase.KingCall => Declarer,
		GamePhase.Talon => Declarer,
		GamePhase.Announcing => Announcements!.CurrentSeat,
		GamePhase.Play => Tricks!.CurrentSeat,
		_ => null
	};

	public bool IsDeclaringSide(int seat) => seat == Declarer || seat == PartnerSeat;

	public static HandState Create(int seed, bool compulsoryKlop = false) => new(seed, compulsoryKlop);

	/// <summary>Rebuild a hand from its event log. The result is indistinguishable from the original.</summary>
	public static HandState Replay(IReadOnlyList<GameEvent> events) {
		ArgumentNullException.ThrowIfNull(events);

		if (events.Count == 0 || events[0] is not HandDealt dealt) {
			throw new ArgumentException("An event log starts with the deal.", nameof(events));
		}

		HandState hand = Create(dealt.Seed, dealt.CompulsoryKlop);

		foreach (GameEvent next in events.Skip(1)) {
			switch (next) {
				case BidPlaced bid: hand.PlaceBid(bid.Bidder, bid.Contract); break;
				case BidPassed pass: hand.PassBid(pass.Bidder); break;
				case KingCalled king: hand.CallKing(king.Declarer, king.Suit); break;
				case TalonPacketTaken packet: hand.TakeTalonPacket(packet.Declarer, packet.Packet); break;
				case CardsDiscarded discard: hand.Discard(discard.Declarer, discard.Cards); break;
				case ContractUpgraded upgrade: hand.UpgradeToColourValat(upgrade.Declarer); break;
				case ContractKept kept: hand.KeepContract(kept.Declarer); break;
				case BonusAnnounced bonus: hand.Announce(bonus.Announcer, bonus.Bonus); break;
				case KontraCalled kontra: hand.Kontra(kontra.Caller, kontra.Target); break;
				case AnnouncementPassed pass: hand.PassAnnouncement(pass.Announcer); break;
				case CardPlayed card: hand.PlayCard(card.Player, card.Card); break;
				default: throw new ArgumentException($"Unknown event {next.GetType().Name}.", nameof(events));
			}
		}

		return hand;
	}

	// --- bidding ---

	public void PlaceBid(int seat, Contract contract) {
		Require(GamePhase.Bidding);
		Bidding.Place(seat, contract);
		_events.Add(new BidPlaced(seat, contract));

		if (Bidding.IsComplete) {
			LeaveBidding();
		}
	}

	public void PassBid(int seat) {
		Require(GamePhase.Bidding);
		Bidding.Pass(seat);
		_events.Add(new BidPassed(seat));

		if (Bidding.IsComplete) {
			LeaveBidding();
		}
	}

	// --- calling a king ---

	/// <summary>The suits the declarer may call. Every plain suit is allowed, your own king included.</summary>
	public IReadOnlyList<Suit> LegalKingCalls() => Phase == GamePhase.KingCall
		? [Suit.Clubs, Suit.Spades, Suit.Hearts, Suit.Diamonds]
		: [];

	public void CallKing(int seat, Suit suit) {
		Require(GamePhase.KingCall);
		RequireSeat(seat);

		KingCall = Dealing.KingCall.Resolve(
			suit,
			seat,
			[Deal.Hand(0), Deal.Hand(1), Deal.Hand(2), Deal.Hand(3)],
			Deal.Talon);

		_events.Add(new KingCalled(seat, suit));
		EnterTalon();
	}

	// --- talon ---

	public void TakeTalonPacket(int seat, int packet) {
		Require(GamePhase.Talon);
		RequireSeat(seat);

		Talon!.ChoosePacket(packet);
		_events.Add(new TalonPacketTaken(seat, packet));
	}

	public void Discard(int seat, IEnumerable<Card> cards) {
		Require(GamePhase.Talon);
		RequireSeat(seat);

		List<Card> chosen = [.. cards];
		Talon!.Discard(chosen);
		_events.Add(new CardsDiscarded(seat, chosen));

		if (!AwaitsUpgradeDecision) {
			LeaveTalon();
		}
	}

	/// <summary>
	/// True while the hand waits for the declarer of a solo to say whether the exchange has made a
	/// barvni valat out of it. The hand does not move on until they answer one way or the other.
	/// </summary>
	public bool AwaitsUpgradeDecision =>
		Phase == GamePhase.Talon
		&& Talon is { IsComplete: true }
		&& !_upgradeDecided
		&& Contracts.CanUpgradeToColourValat(Bidding.FinalContract);

	/// <summary>Lift the solo to a barvni valat.</summary>
	public void UpgradeToColourValat(int seat) {
		RequireUpgradeDecision(seat);

		_upgrade = Contract.ColourValat;
		_upgradeDecided = true;
		_events.Add(new ContractUpgraded(seat, Contract.ColourValat));
		LeaveTalon();
	}

	/// <summary>Stay with the contract that was bid.</summary>
	public void KeepContract(int seat) {
		RequireUpgradeDecision(seat);

		_upgradeDecided = true;
		_events.Add(new ContractKept(seat));
		LeaveTalon();
	}

	// --- announcements ---

	public void Announce(int seat, Bonus bonus) {
		Require(GamePhase.Announcing);
		Announcements!.Announce(seat, bonus);
		_events.Add(new BonusAnnounced(seat, bonus));
	}

	public void Kontra(int seat, KontraTarget target) {
		Require(GamePhase.Announcing);
		Announcements!.Kontra(seat, target);
		_events.Add(new KontraCalled(seat, target));
	}

	public void PassAnnouncement(int seat) {
		Require(GamePhase.Announcing);
		Announcements!.Pass(seat);
		_events.Add(new AnnouncementPassed(seat));

		if (Announcements.IsComplete) {
			EnterPlay();
		}
	}

	// --- play ---

	public void PlayCard(int seat, Card card) {
		Require(GamePhase.Play);
		Tricks!.Play(seat, card);
		_events.Add(new CardPlayed(seat, card));

		if (Tricks.IsComplete) {
			Finish();
		}
	}

	// --- transitions ---

	private void LeaveBidding() {
		if (Info!.CallsKing) {
			Phase = GamePhase.KingCall;
			return;
		}

		EnterTalon();
	}

	private void EnterTalon() {
		Phase = GamePhase.Talon;
		Talon = TalonPhase.Create(PlayedContract!.Value, Deal.Talon, Deal.Hand(Declarer!.Value));

		if (Talon.IsComplete && !AwaitsUpgradeDecision) {
			LeaveTalon();
		}
	}

	private void LeaveTalon() {
		// Klop has nothing to announce and cannot be doubled, so it goes straight to the cards.
		if (Info!.IsKlop) {
			EnterPlay();
			return;
		}

		Phase = GamePhase.Announcing;
		Announcements = AnnouncementRound.Start(
			Info,
			SidesOfSeats(),
			Declarer!.Value,
			[Deal.Hand(0), Deal.Hand(1), Deal.Hand(2), Deal.Hand(3)],
			KingCall?.King);
	}

	private void EnterPlay() {
		Phase = GamePhase.Play;

		IReadOnlyList<Card>[] hands = new IReadOnlyList<Card>[TarokConstants.PlayerCount];
		for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
			hands[seat] = seat == Declarer && Talon!.ChosenPacket is not null ? Talon.Hand : Deal.Hand(seat);
		}

		Tricks = TrickPlay.Start(
			Info!,
			hands,
			Info.ForehandLeads ? BiddingState.ForehandSeat : Declarer!.Value,
			Info.IsKlop ? Talon!.KlopGifts : null,
			new PlayContext { DeclarerSeat = Declarer!.Value, PartnerSeat = PartnerSeat });
	}

	private void Finish() {
		Phase = GamePhase.Finished;

		if (Info!.IsKlop) {
			Score = HandScorer.Score(new HandScoringInput {
				Info = Info,
				DeclarerSeat = Declarer!.Value,
				Tricks = Tricks!.Tricks,
				KlopPiles = [Tricks.Won(0), Tricks.Won(1), Tricks.Won(2), Tricks.Won(3)]
			});

			return;
		}

		List<Card> declaring = [];
		List<Card> defending = [];

		for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
			(IsDeclaringSide(seat) ? declaring : defending).AddRange(Tricks!.Won(seat));
		}

		declaring.AddRange(Talon!.Discards);
		defending.AddRange(Talon.OpponentTalon);

		Score = HandScorer.Score(new HandScoringInput {
			Info = Info,
			DeclarerSeat = Declarer!.Value,
			PartnerSeat = PartnerSeat,
			DeclaringPile = declaring,
			DefendingPile = defending,
			Tricks = Tricks!.Tricks,
			CapturedMondSeats = Tricks.CapturedMondSeats,
			Announcements = Announcements,
			CalledKing = KingCall?.King
		});
	}

	private int[] SidesOfSeats() {
		int[] sides = new int[TarokConstants.PlayerCount];
		for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
			sides[seat] = IsDeclaringSide(seat) ? Sides.Declaring : Sides.Defending;
		}

		return sides;
	}

	private void Require(GamePhase phase) {
		if (Phase != phase) {
			throw new InvalidOperationException($"The hand is in {Phase}, not {phase}.");
		}
	}

	private void RequireUpgradeDecision(int seat) {
		RequireSeat(seat);

		if (!AwaitsUpgradeDecision) {
			throw new InvalidOperationException("There is no barvni valat decision to make here.");
		}
	}

	private void RequireSeat(int seat) {
		if (seat != CurrentSeat) {
			throw new InvalidOperationException($"It is seat {CurrentSeat}'s turn, not seat {seat}'s.");
		}
	}
}
