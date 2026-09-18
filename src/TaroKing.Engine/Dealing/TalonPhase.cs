using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;

namespace TaroKing.Engine.Dealing;

/// <summary>
/// What happens to the six talon cards once the contract is known.
///
/// In three, two and one (and their solos) the talon is laid out in equal packets — two of three,
/// three of two, or six single cards — the declarer picks one packet, takes it into hand and lays the
/// same number of cards away into their own trick pile. Five-point cards (kings, škis, mond, pagat)
/// may never be laid away; a trump that is laid away is shown, so everybody knows how many went down.
/// The packets the declarer did not take go to the opponents.
///
/// In beggar, solo brez and the valats the talon is never seen and goes straight to the opponents.
/// In klop nobody exchanges anything: the six cards are turned up one at a time as a gift to the
/// winner of each of the first six tricks.
/// </summary>
public sealed class TalonPhase {

	private readonly List<Card> _discards = [];
	private readonly List<Card> _hand;
	private readonly List<Card> _opponentTalon = [];
	private readonly List<Card[]> _packets = [];
	private readonly Card[] _talon;

	private TalonPhase(Contract contract, IReadOnlyList<Card> talon, IReadOnlyList<Card> declarerHand) {
		Contract = contract;
		Info = Contracts.Info(contract);
		_talon = [.. talon];
		_hand = [.. declarerHand];

		if (Info.IsKlop) {
			// The talon is dealt out as gifts during play; nothing to choose now.
			IsComplete = true;
			return;
		}

		if (Info.TalonCards == 0) {
			_opponentTalon.AddRange(_talon);
			IsComplete = true;
			return;
		}

		for (int start = 0; start < _talon.Length; start += Info.TalonCards) {
			_packets.Add(_talon[start..(start + Info.TalonCards)]);
		}
	}

	public Contract Contract { get; }

	public ContractInfo Info { get; }

	/// <summary>The packets on offer. Empty when this contract does not touch the talon.</summary>
	public IReadOnlyList<IReadOnlyList<Card>> Packets => _packets;

	/// <summary>Which packet the declarer took, or null before the choice.</summary>
	public int? ChosenPacket { get; private set; }

	/// <summary>The declarer's cards: twelve again once the lay-away is done.</summary>
	public IReadOnlyList<Card> Hand {
		get {
			_hand.Sort();
			return _hand;
		}
	}

	/// <summary>Cards laid away into the declarer's own trick pile; they count for the declaring side.</summary>
	public IReadOnlyList<Card> Discards => _discards;

	/// <summary>Laid-away trumps, which are shown to the table rather than hidden.</summary>
	public IReadOnlyList<Card> ShownDiscards => _discards.Where(card => card.IsTrump).ToList();

	/// <summary>Talon cards the declarer did not take; they count for the opponents.</summary>
	public IReadOnlyList<Card> OpponentTalon => _opponentTalon;

	/// <summary>The six talon cards handed to the winners of the first six tricks in klop.</summary>
	public IReadOnlyList<Card> KlopGifts => Info.IsKlop ? _talon : Array.Empty<Card>();

	/// <summary>True once there is nothing left to do before play starts.</summary>
	public bool IsComplete { get; private set; }

	public bool NeedsPacketChoice => _packets.Count > 0 && ChosenPacket is null;

	public bool NeedsDiscard => ChosenPacket is not null && !IsComplete;

	public static TalonPhase Create(Contract contract, IReadOnlyList<Card> talon, IReadOnlyList<Card> declarerHand) {
		ArgumentNullException.ThrowIfNull(talon);
		ArgumentNullException.ThrowIfNull(declarerHand);

		if (talon.Count != TarokConstants.TalonSize) {
			throw new ArgumentException($"The talon holds {TarokConstants.TalonSize} cards.", nameof(talon));
		}

		if (declarerHand.Count != TarokConstants.HandSize) {
			throw new ArgumentException($"A hand holds {TarokConstants.HandSize} cards.", nameof(declarerHand));
		}

		if (talon.Concat(declarerHand).Distinct().Count() != talon.Count + declarerHand.Count) {
			throw new ArgumentException("The talon and the declarer's hand share a card.", nameof(talon));
		}

		return new TalonPhase(contract, talon, declarerHand);
	}

	/// <summary>Take one packet into hand; the rest of the talon goes to the opponents.</summary>
	public void ChoosePacket(int index) {
		if (_packets.Count == 0) {
			throw new InvalidOperationException($"{Info.SlovenianName} does not use the talon.");
		}

		if (ChosenPacket is not null) {
			throw new InvalidOperationException("A packet has already been taken.");
		}

		ArgumentOutOfRangeException.ThrowIfNegative(index);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _packets.Count);

		ChosenPacket = index;
		_hand.AddRange(_packets[index]);

		for (int packet = 0; packet < _packets.Count; packet++) {
			if (packet != index) {
				_opponentTalon.AddRange(_packets[packet]);
			}
		}
	}

	/// <summary>The cards in hand that may be laid away: everything except the five-pointers.</summary>
	public IReadOnlyList<Card> LegalDiscards() {
		if (!NeedsDiscard) {
			return [];
		}

		return _hand.Where(card => !card.IsFivePointer).OrderBy(card => card).ToList();
	}

	/// <summary>Lay away as many cards as were taken.</summary>
	public void Discard(IEnumerable<Card> cards) {
		ArgumentNullException.ThrowIfNull(cards);

		if (!NeedsDiscard) {
			throw new InvalidOperationException("There is nothing to lay away.");
		}

		List<Card> chosen = [.. cards];

		if (chosen.Count != Info.TalonCards) {
			throw new ArgumentException($"{Info.SlovenianName} lays away {Info.TalonCards} cards, not {chosen.Count}.", nameof(cards));
		}

		if (chosen.Distinct().Count() != chosen.Count) {
			throw new ArgumentException("The same card cannot be laid away twice.", nameof(cards));
		}

		foreach (Card card in chosen) {
			if (!_hand.Contains(card)) {
				throw new ArgumentException($"The declarer does not hold the {card}.", nameof(cards));
			}

			if (card.IsFivePointer) {
				throw new ArgumentException($"The {card} is worth five and may not be laid away.", nameof(cards));
			}
		}

		foreach (Card card in chosen) {
			_hand.Remove(card);
		}

		_discards.AddRange(chosen);
		IsComplete = true;
	}
}
