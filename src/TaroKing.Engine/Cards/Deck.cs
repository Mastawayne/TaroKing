using TaroKing.Engine.Randomness;

namespace TaroKing.Engine.Cards;

/// <summary>The 54-card tarok pack.</summary>
public static class Deck {

	private static readonly Card[] Ordered = BuildOrdered();

	/// <summary>
	/// The full pack in canonical order: clubs, spades, hearts, diamonds (weakest to king),
	/// then trumps I to XXI and the škis.
	/// </summary>
	public static IReadOnlyList<Card> Full() => Ordered;

	/// <summary>A shuffled copy of the pack. The same seed always produces the same order.</summary>
	public static Card[] Shuffled(int seed) => Shuffled(new Pcg32(seed));

	/// <summary>A shuffled copy of the pack, drawn from an existing generator.</summary>
	public static Card[] Shuffled(Pcg32 random) {
		ArgumentNullException.ThrowIfNull(random);

		Card[] cards = Ordered.ToArray();
		random.Shuffle(cards);
		return cards;
	}

	private static Card[] BuildOrdered() {
		List<Card> cards = new(TarokConstants.DeckSize);

		foreach (Suit suit in new[] { Suit.Clubs, Suit.Spades, Suit.Hearts, Suit.Diamonds }) {
			for (int rank = 1; rank <= TarokConstants.SuitSize; rank++) {
				cards.Add(new Card(suit, rank));
			}
		}

		for (int rank = 1; rank <= TarokConstants.TrumpCount; rank++) {
			cards.Add(new Card(Suit.Trump, rank));
		}

		return cards.ToArray();
	}
}
