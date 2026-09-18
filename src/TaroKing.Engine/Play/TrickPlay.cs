using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;

namespace TaroKing.Engine.Play;

/// <summary>
/// The twelve tricks of one hand. The engine owns every hand of cards, hands out only the legal
/// moves for the seat on turn, and refuses anything else — a client can ask, it cannot assert.
/// </summary>
public sealed class TrickPlay {

	private readonly List<int> _capturedMondSeats = [];
	private readonly List<TrickCard> _current = [];
	private readonly List<Card>[] _hands;
	private readonly Card[] _klopGifts;
	private readonly List<Trick> _tricks = [];
	private readonly List<Card>[] _won;

	private TrickPlay(ContractInfo info, IReadOnlyList<IReadOnlyList<Card>> hands, int leadSeat, IReadOnlyList<Card> klopGifts) {
		Info = info;
		CurrentSeat = leadSeat;
		LeadSeat = leadSeat;
		_klopGifts = [.. klopGifts];

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

	/// <summary>1 to 12 while the hand runs; 13 once it is over.</summary>
	public int TrickNumber => _tricks.Count + 1;

	/// <summary>The cards already played to the trick in progress.</summary>
	public IReadOnlyList<TrickCard> CurrentTrick => _current;

	public IReadOnlyList<Trick> Tricks => _tricks;

	public bool IsComplete => _tricks.Count == TarokConstants.TrickCount;

	/// <summary>Seats that lost the mond to the škis; each of them takes a personal penalty.</summary>
	public IReadOnlyList<int> CapturedMondSeats => _capturedMondSeats;

	public static TrickPlay Start(
		ContractInfo info,
		IReadOnlyList<IReadOnlyList<Card>> hands,
		int leadSeat,
		IReadOnlyList<Card>? klopGifts = null) {

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

		return new TrickPlay(info, hands, leadSeat, klopGifts ?? []);
	}

	/// <summary>The cards a seat still holds.</summary>
	public IReadOnlyList<Card> Hand(int seat) {
		ValidateSeat(seat);
		return _hands[seat];
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
	}

	private static void ValidateSeat(int seat) {
		ArgumentOutOfRangeException.ThrowIfNegative(seat);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(seat, TarokConstants.PlayerCount);
	}
}
