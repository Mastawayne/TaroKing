namespace TaroKing.Engine.Bidding;

/// <summary>One entry in the auction record: a contract named by a seat, or a pass.</summary>
public readonly record struct BidEntry(int Seat, Contract? Bid) {

	public bool IsPass => Bid is null;

	public override string ToString() => IsPass
		? $"seat {Seat}: dalje"
		: $"seat {Seat}: {Contracts.Info(Bid!.Value).SlovenianName}";
}

/// <summary>
/// The auction. Seats are numbered in playing order with seat 0 = forehand, seat 3 = dealer;
/// that order is also the order of priority, forehand highest.
///
/// Forehand says nothing to begin with, so bidding opens with seat 1 and the lowest bid available
/// is <see cref="Contract.Two"/>. Klop and three belong to forehand alone, and only when the other
/// three have all passed. A player who has passed is out. The auction ends once everyone except one
/// bidder has passed, and that bidder becomes declarer.
/// </summary>
public sealed class BiddingState {

	/// <summary>The seat that leads the auction's priority order and, in most contracts, the first trick.</summary>
	public const int ForehandSeat = 0;

	private readonly List<BidEntry> _history = [];
	private readonly bool[] _passed = new bool[TarokConstants.PlayerCount];

	private BiddingState() {
		CurrentSeat = 1;
	}

	/// <summary>A fresh auction, ready for seat 1 to speak.</summary>
	public static BiddingState Start() => new();

	/// <summary>
	/// The auction that follows a hand where somebody held no trump: it is not played out at all,
	/// the hand is dealt again and played as a compulsory klop by forehand.
	/// </summary>
	public static BiddingState CompulsoryKlop() {
		BiddingState state = new();
		state.WinningBid = Contract.Klop;
		state.WinningSeat = ForehandSeat;
		state.IsComplete = true;
		state.IsCompulsoryKlop = true;
		return state;
	}

	/// <summary>Whose turn it is to bid. Meaningless once <see cref="IsComplete"/>.</summary>
	public int CurrentSeat { get; private set; }

	public bool IsComplete { get; private set; }

	/// <summary>True when this klop was forced by a hand without trumps rather than bid.</summary>
	public bool IsCompulsoryKlop { get; private set; }

	/// <summary>The highest contract named so far, and who named it.</summary>
	public Contract? WinningBid { get; private set; }

	public int? WinningSeat { get; private set; }

	public IReadOnlyList<BidEntry> History => _history;

	/// <summary>
	/// True when the other three have passed without a bid: forehand now names any contract at all,
	/// and this is the only moment klop or three can be chosen. Forehand may not pass here.
	/// </summary>
	public bool IsForehandPrivilege =>
		!IsComplete
		&& WinningBid is null
		&& _passed[1] && _passed[2] && _passed[3];

	/// <summary>The declarer. Only valid once the auction is complete.</summary>
	public int Declarer => IsComplete
		? WinningSeat!.Value
		: throw new InvalidOperationException("The auction is not finished.");

	/// <summary>The contract that will be played. Only valid once the auction is complete.</summary>
	public Contract FinalContract => IsComplete
		? WinningBid!.Value
		: throw new InvalidOperationException("The auction is not finished.");

	/// <summary>The rules of the contract that will be played.</summary>
	public ContractInfo FinalContractInfo => Contracts.Info(FinalContract);

	public bool HasPassed(int seat) {
		ValidateSeat(seat);
		return _passed[seat];
	}

	/// <summary>Whether the seat on turn is allowed to pass. Only forehand's privilege forbids it.</summary>
	public bool CanPass() => !IsComplete && !IsForehandPrivilege;

	/// <summary>
	/// What the seat on turn may bid. Empty means the only legal action is to pass —
	/// which happens when valat already stands and the bidder outranks you.
	/// </summary>
	public IReadOnlyList<Contract> LegalBids() {
		if (IsComplete) {
			return [];
		}

		if (IsForehandPrivilege) {
			return Contracts.From(Contract.Klop);
		}

		if (WinningBid is null) {
			return Contracts.From(Contracts.LowestOpenBid);
		}

		// Priority runs forehand (0) down to dealer (3). A senior player may match the standing bid,
		// a junior one has to beat it.
		bool mayMatch = CurrentSeat < WinningSeat!.Value;
		int lowest = (int)WinningBid.Value + (mayMatch ? 0 : 1);

		return lowest > (int)Contracts.Highest ? [] : Contracts.From((Contract)lowest);
	}

	/// <summary>Pass ("dalje").</summary>
	public void Pass(int seat) {
		RequireTurn(seat);

		if (IsForehandPrivilege) {
			throw new InvalidOperationException("Forehand must name a contract when everyone else has passed.");
		}

		_passed[seat] = true;
		_history.Add(new BidEntry(seat, null));
		Settle();
	}

	/// <summary>Name a contract.</summary>
	public void Place(int seat, Contract contract) {
		RequireTurn(seat);

		if (!LegalBids().Contains(contract)) {
			throw new InvalidOperationException($"Seat {seat} may not bid {Contracts.Info(contract).SlovenianName} here.");
		}

		bool privilege = IsForehandPrivilege;

		WinningBid = contract;
		WinningSeat = seat;
		_history.Add(new BidEntry(seat, contract));

		if (privilege) {
			// Nobody is left to overcall it.
			IsComplete = true;
			return;
		}

		Settle();
	}

	private void Settle() {
		if (WinningBid is not null && EveryoneElseHasPassed(WinningSeat!.Value)) {
			IsComplete = true;
			return;
		}

		MoveToNextSeat();
	}

	private bool EveryoneElseHasPassed(int bidder) {
		for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
			if (seat != bidder && !_passed[seat]) {
				return false;
			}
		}

		return true;
	}

	private void MoveToNextSeat() {
		for (int step = 1; step <= TarokConstants.PlayerCount; step++) {
			int seat = (CurrentSeat + step) % TarokConstants.PlayerCount;
			if (!_passed[seat]) {
				CurrentSeat = seat;
				return;
			}
		}

		throw new InvalidOperationException("Every seat has passed without a bid; the auction cannot continue.");
	}

	private void RequireTurn(int seat) {
		ValidateSeat(seat);

		if (IsComplete) {
			throw new InvalidOperationException("The auction is already finished.");
		}

		if (seat != CurrentSeat) {
			throw new InvalidOperationException($"It is seat {CurrentSeat}'s turn, not seat {seat}'s.");
		}
	}

	private static void ValidateSeat(int seat) {
		ArgumentOutOfRangeException.ThrowIfNegative(seat);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(seat, TarokConstants.PlayerCount);
	}
}
