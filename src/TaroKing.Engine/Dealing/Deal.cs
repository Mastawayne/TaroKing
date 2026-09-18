using TaroKing.Engine.Cards;

namespace TaroKing.Engine.Dealing;

/// <summary>
/// One dealt hand of four-player tarok: six cards to the talon, then packets of six to each seat
/// until everybody holds twelve. Seats are numbered in playing order, seat 0 being forehand
/// (the dealer's right-hand neighbour, who is dealt to first and leads the first trick).
/// </summary>
public sealed class Deal {

	private const int PacketSize = 6;

	private readonly Card[][] _hands;
	private readonly Card[] _talon;

	private Deal(int seed, Card[] talon, Card[][] hands, int[] seatsWithoutTrump) {
		Seed = seed;
		_talon = talon;
		_hands = hands;
		SeatsWithoutTrump = seatsWithoutTrump;
	}

	/// <summary>The seed the pack was shuffled with. 0 for a deal built from an explicit pack.</summary>
	public int Seed { get; }

	/// <summary>The six face-down talon cards, in the order they were dealt.</summary>
	public IReadOnlyList<Card> Talon => _talon;

	/// <summary>Seats that were dealt no trump at all, and may demand a redeal into a compulsory klop.</summary>
	public IReadOnlyList<int> SeatsWithoutTrump { get; }

	/// <summary>True when at least one seat holds no trump: the hand is annulled and dealt again.</summary>
	public bool RequiresRedeal => SeatsWithoutTrump.Count > 0;

	/// <summary>The twelve cards held by a seat, sorted for display (suit, then rank).</summary>
	public IReadOnlyList<Card> Hand(int seat) {
		ArgumentOutOfRangeException.ThrowIfNegative(seat);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(seat, TarokConstants.PlayerCount);

		return _hands[seat];
	}

	/// <summary>Deal from a shuffled pack. The same seed always produces the same deal.</summary>
	public static Deal Create(int seed) => FromOrderedPack(Deck.Shuffled(seed), seed);

	/// <summary>
	/// Deal from a pack in a given order — the shuffle has already happened, this only distributes.
	/// Used by tests and by replays, where the pack order comes from the saved game rather than a seed.
	/// </summary>
	public static Deal FromOrderedPack(IReadOnlyList<Card> pack, int seed = 0) {
		ArgumentNullException.ThrowIfNull(pack);

		if (pack.Count != TarokConstants.DeckSize) {
			throw new ArgumentException($"A pack must hold {TarokConstants.DeckSize} cards, got {pack.Count}.", nameof(pack));
		}

		if (pack.Distinct().Count() != TarokConstants.DeckSize) {
			throw new ArgumentException("A pack must not contain duplicate cards.", nameof(pack));
		}

		int next = 0;

		Card[] talon = new Card[TarokConstants.TalonSize];
		for (int i = 0; i < talon.Length; i++) {
			talon[i] = pack[next++];
		}

		Card[][] hands = new Card[TarokConstants.PlayerCount][];
		for (int seat = 0; seat < hands.Length; seat++) {
			hands[seat] = new Card[TarokConstants.HandSize];
		}

		int packetsPerSeat = TarokConstants.HandSize / PacketSize;
		for (int packet = 0; packet < packetsPerSeat; packet++) {
			for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
				for (int i = 0; i < PacketSize; i++) {
					hands[seat][(packet * PacketSize) + i] = pack[next++];
				}
			}
		}

		List<int> seatsWithoutTrump = [];
		for (int seat = 0; seat < hands.Length; seat++) {
			if (!Array.Exists(hands[seat], card => card.IsTrump)) {
				seatsWithoutTrump.Add(seat);
			}

			Array.Sort(hands[seat]);
		}

		return new Deal(seed, talon, hands, seatsWithoutTrump.ToArray());
	}
}
