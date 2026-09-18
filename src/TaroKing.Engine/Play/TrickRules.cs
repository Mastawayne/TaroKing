using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;

namespace TaroKing.Engine.Play;

/// <summary>
/// The rules of a single trick, as pure functions: what you are allowed to play, and who takes it.
///
/// Ordinary contracts: follow the led suit if you can, trump if you cannot, otherwise play anything.
/// The highest trump takes the trick, or the highest card of the led suit if no trump fell.
///
/// Negative contracts (klop, berač, odprti berač): the same, but you must also beat whatever is
/// currently winning if any of your legal cards can, and the pagat is held back — it may only be
/// played when nothing else is legal.
///
/// Colour valat: trumps stop being trumps. They are an ordinary suit that only wins when it is led.
/// </summary>
public static class TrickRules {

	/// <summary>The cards this hand may play into the trick so far. An empty trick means leading.</summary>
	public static IReadOnlyList<Card> LegalPlays(ContractInfo info, IReadOnlyList<Card> hand, IReadOnlyList<TrickCard> trick) {
		ArgumentNullException.ThrowIfNull(info);
		ArgumentNullException.ThrowIfNull(hand);
		ArgumentNullException.ThrowIfNull(trick);

		if (hand.Count == 0) {
			return [];
		}

		if (trick.Count == 0) {
			return HoldBackPagat(info, hand);
		}

		Suit led = trick[0].Card.Suit;
		List<Card> candidates = hand.Where(card => card.Suit == led).ToList();

		if (candidates.Count == 0 && !info.TrumpsArePlainSuit) {
			// Cannot follow: trumping is compulsory while you still hold a trump.
			candidates = hand.Where(card => card.IsTrump).ToList();
		}

		if (candidates.Count == 0) {
			candidates = [.. hand];
		}

		if (info.UsesNegativePlayRules) {
			candidates = MustBeatTheTable(info, candidates, trick);
		}

		return HoldBackPagat(info, candidates);
	}

	/// <summary>Which seat is winning the cards played so far. Works on a part-played trick too.</summary>
	public static int Winner(ContractInfo info, IReadOnlyList<TrickCard> trick) {
		ArgumentNullException.ThrowIfNull(info);
		ArgumentNullException.ThrowIfNull(trick);

		if (trick.Count == 0) {
			throw new ArgumentException("An empty trick has no winner.", nameof(trick));
		}

		Suit led = trick[0].Card.Suit;

		// In colour valat trumps only count when trumps were led; otherwise they are so much waste paper.
		bool trumpsDecide = info.TrumpsArePlainSuit
			? led == Suit.Trump
			: trick.Any(played => played.Card.IsTrump);

		if (trumpsDecide) {
			if (IsEmperorTrick(trick)) {
				// Škis, mond and pagat together: the pagat steals it.
				return trick.First(played => played.Card.IsPagat).Seat;
			}

			return Highest(trick.Where(played => played.Card.IsTrump));
		}

		return Highest(trick.Where(played => played.Card.Suit == led));
	}

	/// <summary>The card currently winning the trick.</summary>
	public static Card WinningCard(ContractInfo info, IReadOnlyList<TrickCard> trick) {
		int seat = Winner(info, trick);
		return trick.First(played => played.Seat == seat).Card;
	}

	/// <summary>Škis, mond and pagat all in one trick — the "cesarski štih", which the pagat takes.</summary>
	public static bool IsEmperorTrick(IReadOnlyList<TrickCard> trick) {
		ArgumentNullException.ThrowIfNull(trick);

		bool skis = false;
		bool mond = false;
		bool pagat = false;

		foreach (TrickCard played in trick) {
			skis |= played.Card.IsSkis;
			mond |= played.Card.IsMond;
			pagat |= played.Card.IsPagat;
		}

		return skis && mond && pagat;
	}

	/// <summary>
	/// The seat that let the mond be captured by the škis, which costs that player 20 personally.
	/// It applies in the contracts decided on card points, and applies even when the pagat steals
	/// the trick or the captor is the mond holder's own partner.
	/// </summary>
	public static int? CapturedMondSeat(ContractInfo info, IReadOnlyList<TrickCard> trick) {
		ArgumentNullException.ThrowIfNull(info);
		ArgumentNullException.ThrowIfNull(trick);

		if (!info.CountsCardPoints) {
			return null;
		}

		if (!trick.Any(played => played.Card.IsSkis)) {
			return null;
		}

		foreach (TrickCard played in trick) {
			if (played.Card.IsMond) {
				return played.Seat;
			}
		}

		return null;
	}

	/// <summary>True when <paramref name="candidate"/> would take the trick away from <paramref name="winning"/>.</summary>
	private static bool Beats(ContractInfo info, Card candidate, Card winning) {
		if (candidate.Suit == winning.Suit) {
			return candidate.Rank > winning.Rank;
		}

		return !info.TrumpsArePlainSuit && candidate.IsTrump && !winning.IsTrump;
	}

	private static List<Card> MustBeatTheTable(ContractInfo info, List<Card> candidates, IReadOnlyList<TrickCard> trick) {
		Card winning = WinningCard(info, trick);
		List<Card> better = candidates.Where(card => Beats(info, card, winning)).ToList();

		return better.Count > 0 ? better : candidates;
	}

	private static IReadOnlyList<Card> HoldBackPagat(ContractInfo info, IReadOnlyList<Card> candidates) {
		if (!info.UsesNegativePlayRules || candidates.Count <= 1) {
			return candidates;
		}

		List<Card> withoutPagat = candidates.Where(card => !card.IsPagat).ToList();
		return withoutPagat.Count > 0 ? withoutPagat : candidates;
	}

	private static int Highest(IEnumerable<TrickCard> played) {
		TrickCard best = played.First();
		foreach (TrickCard candidate in played) {
			if (candidate.Card.Rank > best.Card.Rank) {
				best = candidate;
			}
		}

		return best.Seat;
	}
}
