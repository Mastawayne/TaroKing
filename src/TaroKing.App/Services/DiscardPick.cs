using TaroKing.Engine.Cards;

namespace TaroKing.App.Services;

/// <summary>
/// The cards a player has picked out to lay away, before the choice is sent. The hand on the board
/// shows the selection and the action strip submits it, so the two share this one small object
/// rather than each keeping a copy.
/// </summary>
public sealed class DiscardPick {

	private readonly List<Card> _cards = [];

	public IReadOnlyList<Card> Cards => _cards;

	public int Count => _cards.Count;

	public bool Contains(Card card) => _cards.Contains(card);

	/// <summary>Adds the card, or removes it if it was already picked; never more than <paramref name="limit"/>.</summary>
	public void Toggle(Card card, int limit) {
		if (!_cards.Remove(card) && _cards.Count < limit) {
			_cards.Add(card);
		}
	}

	public void Clear() => _cards.Clear();

	/// <summary>Hands the selection over and starts afresh.</summary>
	public Card[] Take() {
		Card[] chosen = [.. _cards];
		_cards.Clear();

		return chosen;
	}
}
