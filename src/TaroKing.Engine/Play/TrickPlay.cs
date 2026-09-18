using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;

namespace TaroKing.Engine.Play;

/// <summary>Who is playing for whom, which is what lets a hand be decided before all twelve tricks.</summary>
public sealed record PlayContext {

	public required int DeclarerSeat { get; init; }

	/// <summary>The called king's holder, when the declarer has a partner.</summary>
	public int? PartnerSeat { get; init; }

	public bool IsDeclaringSide(int seat) => seat == DeclarerSeat || seat == PartnerSeat;
}

/// <summary>How a hand finished.</summary>
public enum HandEnding {
	InProgress = 0,

	/// <summary>All twelve tricks were played out.</summary>
	AllTricksPlayed = 1,

	/// <summary>A berač or odprti berač took a trick, so there is nothing left to play for.</summary>
	NegativeContractBroken = 2,

	/// <summary>A valat lost a trick, so there is nothing left to play for.</summary>
	ValatBroken = 3
}

/// <summary>
/// The twelve tricks of one hand. The engine owns every hand of cards, hands out only the legal
/// moves for the seat on turn, and refuses anything else — a client can ask, it cannot assert.
///
/// Given a <see cref="PlayContext"/> it also knows when to stop early: a berač that takes a trick
/// and a valat that loses one are both decided on the spot, and the remaining cards are not played.
/// </summary>
public sealed class TrickPlay {

	private readonly List<int> _capturedMondSeats = [];
	private readonly PlayContext? _context;
	private readonly List<TrickCard> _current = [];
	private readonly List<Card>[] _hands;
	private readonly Card[] _klopGifts;
	private readonly List<Trick> _tricks = [];
	private readonly List<Card>[] _won;

	private TrickPlay(
		ContractInfo info,
		IReadOnlyList<IReadOnlyList<Card>> hands,
		int leadSeat,
		IReadOnlyList<Card> klopGifts,
		PlayContext? context) {

		Info = info;
		CurrentSeat = leadSeat;
		LeadSeat = leadSeat;
		_klopGifts = [.. klopGifts];
		_context = context;

		_hands = new List<Card>[TarokConstants.PlayerCount];
		_won = new List<Card>[TarokConstants.PlayerCount];
		for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
			_hands[seat] = [.. hands[seat]];
			_won[seat] = [];
		}
	}

	public ContractInfo Info { get; }

	/// <summary>Who led the very first trick.</summary>
	public int LeadSeat { get; }

	/// <summary>The seat to play next.</summary>
	public int CurrentSeat { get; private set; }

	/// <summary>1 to 12 while the hand runs.</summary>
	public int TrickNumber => _tricks.Count + 1;

	/// <summary>The cards already played to the trick in progress.</summary>
	public IReadOnlyList<TrickCard> CurrentTrick => _current;

	public IReadOnlyList<Trick> Tricks => _tricks;

	public HandEnding Ending { get; private set; } = HandEnding.InProgress;

	public bool IsComplete => Ending != HandEnding.InProgress;

	/// <summary>True when the hand stopped before the twelfth trick because it was already decided.</summary>
	public bool EndedEarly => Ending is HandEnding.NegativeContractBroken or HandEnding.ValatBroken;

	/// <summary>Seats that lost the mond to the škis; each of them takes a personal penalty.</summary>
	public IReadOnlyList<int> CapturedMondSeats => _capturedMondSeats;

	public static TrickPlay Start(
		ContractInfo info,
		IReadOnlyList<IReadOnlyList<Card>> hands,
		int leadSeat,
		IReadOnlyList<Card>? klopGifts = null,
		PlayContext? context = null) {

		ArgumentNullException.ThrowIfNull(info);
		ArgumentNullException.ThrowIfNull(hands);
		ValidateSeat(leadSeat);

		if (hands.Count != TarokConstants.PlayerCount) {
			throw new ArgumentException($"Expected {TarokConstants.PlayerCount} hands.", nameof(hands));
		}

		foreach (IReadOnlyList<Card> hand in hands) {
			if (hand.Count != TarokConstants.HandSize) {
				throw new ArgumentException($"Every hand holds {TarokConstants.HandSize} cards.", nameof(hands));
			}
		}

		List<Card> all = [];
		foreach (IReadOnlyList<Card> hand in hands) {
			all.AddRange(hand);
		}

		if (all.Distinct().Count() != all.Count) {
			throw new ArgumentException("The same card was dealt twice.", nameof(hands));
		}

		if (context is not null) {
			ValidateSeat(context.DeclarerSeat);
			if (context.PartnerSeat is int partner) {
				ValidateSeat(partner);
			}
		}

		return new TrickPlay(info, hands, leadSeat, klopGifts ?? [], context);
	}

	/// <summary>The cards a seat still holds.</summary>
	public IReadOnlyList<Card> Hand(int seat) {
		ValidateSeat(seat);
		return _hands[seat];
	}

	/// <summary>
	/// True once a seat's cards are face up on the table: the odprti berač's declarer plays open
	/// from the second trick onwards.
	/// </summary>
	public bool IsHandExposed(int seat) {
		ValidateSeat(seat);

		return Info.Contract == Contract.OpenBeggar
			&& _context is not null
			&& seat == _context.DeclarerSeat
			&& _tricks.Count >= 1;
	}

	/// <summary>Every card a seat has taken in, including klop gifts.</summary>
	public IReadOnlyList<Card> Won(int seat) {
		ValidateSeat(seat);
		return _won[seat];
	}

	public int TricksWonBy(int seat) {
		ValidateSeat(seat);
		return _tricks.Count(trick => trick.WinnerSeat == seat);
	}

	/// <summary>What the seat on turn may play. Empty for anybody else.</summary>
	public IReadOnlyList<Card> LegalPlays(int seat) {
		ValidateSeat(seat);

		if (IsComplete || seat != CurrentSeat) {
			return [];
		}

		return TrickRules.LegalPlays(Info, _hands[seat], _current);
	}

	public void Play(int seat, Card card) {
		ValidateSeat(seat);

		if (IsComplete) {
			throw new InvalidOperationException("The hand is over.");
		}

		if (seat != CurrentSeat) {
			throw new InvalidOperationException($"It is seat {CurrentSeat}'s turn, not seat {seat}'s.");
		}

		if (!_hands[seat].Contains(card)) {
			throw new InvalidOperationException($"Seat {seat} does not hold the {card}.");
		}

		if (!LegalPlays(seat).Contains(card)) {
			throw new InvalidOperationException($"The {card} is not a legal play for seat {seat} here.");
		}

		_hands[seat].Remove(card);
		_current.Add(new TrickCard(seat, card));

		if (_current.Count < TarokConstants.PlayerCount) {
			CurrentSeat = (seat + 1) % TarokConstants.PlayerCount;
			return;
		}

		CompleteTrick();
	}

	private void CompleteTrick() {
		int winner = TrickRules.Winner(Info, _current);
		int number = _tricks.Count + 1;

		int? capturedMond = TrickRules.CapturedMondSeat(Info, _current);
		if (capturedMond is not null) {
			_capturedMondSeats.Add(capturedMond.Value);
		}

		Trick trick = new(number, _current[0].Seat, _current, winner);
		_tricks.Add(trick);
		_won[winner].AddRange(trick.Pile);

		// Klop turns one talon card face up per trick as a present for whoever took it.
		if (Info.IsKlop && number <= _klopGifts.Length) {
			_won[winner].Add(_klopGifts[number - 1]);
		}

		_current.Clear();
		CurrentSeat = winner;

		Ending = DecideEnding(winner);
	}

	private HandEnding DecideEnding(int lastWinner) {
		if (_context is not null) {
			bool declaringSideTookIt = _context.IsDeclaringSide(lastWinner);

			// A berač that takes anything has already failed; so has a valat that drops a trick.
			if (Info.TakesNoTricks && !Info.IsKlop && declaringSideTookIt) {
				return HandEnding.NegativeContractBroken;
			}

			if (Info.TakesAllTricks && !declaringSideTookIt) {
				return HandEnding.ValatBroken;
			}
		}

		return _tricks.Count == TarokConstants.TrickCount
			? HandEnding.AllTricksPlayed
			: HandEnding.InProgress;
	}

	private static void ValidateSeat(int seat) {
		ArgumentOutOfRangeException.ThrowIfNegative(seat);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(seat, TarokConstants.PlayerCount);
	}
}
