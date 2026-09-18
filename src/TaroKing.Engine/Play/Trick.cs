using TaroKing.Engine.Cards;

namespace TaroKing.Engine.Play;

/// <summary>A card as it lies on the table, together with the seat that played it.</summary>
public readonly record struct TrickCard(int Seat, Card Card) {
	public override string ToString() => $"seat {Seat}: {Card}";
}

/// <summary>A finished trick.</summary>
public sealed class Trick {

	private readonly TrickCard[] _cards;

	public Trick(int number, int leadSeat, IReadOnlyList<TrickCard> cards, int winnerSeat) {
		ArgumentNullException.ThrowIfNull(cards);

		if (cards.Count != TarokConstants.PlayerCount) {
			throw new ArgumentException($"A trick holds {TarokConstants.PlayerCount} cards.", nameof(cards));
		}

		Number = number;
		LeadSeat = leadSeat;
		WinnerSeat = winnerSeat;
		_cards = [.. cards];
	}

	/// <summary>1 to 12.</summary>
	public int Number { get; }

	public int LeadSeat { get; }

	public int WinnerSeat { get; }

	/// <summary>The four cards in the order they were played.</summary>
	public IReadOnlyList<TrickCard> Cards => _cards;

	public Suit LedSuit => _cards[0].Card.Suit;

	public Card WinningCard => _cards.First(played => played.Seat == WinnerSeat).Card;

	/// <summary>Just the cards, for counting.</summary>
	public IEnumerable<Card> Pile => _cards.Select(played => played.Card);
}
