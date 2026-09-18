namespace TaroKing.Engine.Cards;

/// <summary>
/// Counting card points the way it is done at the table: in batches of three, subtracting 2 per batch.
/// A leftover of one or two cards is worth one point less than face value. The whole pack comes to 70.
/// </summary>
public static class CardScoring {

	/// <summary>Face value of a set of cards, before the batch deduction.</summary>
	public static int RawPoints(IEnumerable<Card> cards) {
		ArgumentNullException.ThrowIfNull(cards);

		int raw = 0;
		foreach (Card card in cards) {
			raw += card.Points;
		}

		return raw;
	}

	/// <summary>
	/// Counted value of a set of cards. Order does not matter: the deduction depends only on how many
	/// cards there are — 2 per full batch of three, plus 1 for a remainder of one or two cards.
	/// </summary>
	public static int Count(IEnumerable<Card> cards) {
		ArgumentNullException.ThrowIfNull(cards);

		int raw = 0;
		int count = 0;
		foreach (Card card in cards) {
			raw += card.Points;
			count++;
		}

		return raw - Deduction(count);
	}

	/// <summary>The amount subtracted from face value for a pile of <paramref name="cardCount"/> cards.</summary>
	public static int Deduction(int cardCount) {
		ArgumentOutOfRangeException.ThrowIfNegative(cardCount);

		int batches = cardCount / 3;
		int remainder = cardCount % 3;
		return (2 * batches) + (remainder > 0 ? 1 : 0);
	}
}
