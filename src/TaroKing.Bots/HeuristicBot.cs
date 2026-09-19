using TaroKing.Engine.Announcing;
using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;
using TaroKing.Engine.Play;
using TaroKing.Engine.Randomness;
using TaroKing.Engine.Session;

namespace TaroKing.Bots;

/// <summary>
/// A bot that plays by rules of thumb rather than by search: count the hand, bid what it is worth,
/// lay away points into your own pile, draw trumps when you are strong, feed your partner and duck
/// when you are not. It is beatable, but it plays recognisable tarok and it plays it fast.
/// </summary>
public sealed class HeuristicBot : IPlayerAgent {

	private readonly BotProfile _profile;
	private readonly Pcg32 _random;

	public HeuristicBot(int seed, BotProfile profile = BotProfile.Normal, string? name = null) {
		_random = new Pcg32(seed);
		_profile = profile;
		Name = name ?? $"{profile}({seed})";
	}

	public string Name { get; }

	// --- bidding ---

	public ValueTask<Contract?> ChooseBidAsync(PlayerView view, CancellationToken cancellationToken = default) {
		if (view.LegalBids.Count == 0) {
			return ValueTask.FromResult<Contract?>(null);
		}

		double strength = Strength(view.Hand) + Daring;
		Contract? best = null;

		if (view.LegalBids.Contains(Contract.Beggar) && LooksLikeABeggar(view.Hand)) {
			best = Contract.Beggar;
		}

		foreach (Contract contract in view.LegalBids) {
			if (strength >= ThresholdFor(contract)) {
				best = contract;
			}
		}

		// Forehand, with everybody else out, has to name something.
		if (best is null && !view.CanPassBid) {
			best = view.LegalBids.Contains(Contract.Klop) ? Contract.Klop : view.LegalBids[0];
		}

		return ValueTask.FromResult(best);
	}

	/// <summary>
	/// Trumps are the currency, the three five-point trumps are worth a lot more than their count,
	/// and kings are what turn a long hand into a winning one.
	/// </summary>
	public static double Strength(IReadOnlyList<Card> hand) {
		ArgumentNullException.ThrowIfNull(hand);

		double score = hand.Count(card => card.IsTrump);

		score += hand.Count(card => card.IsSkis) * 3;
		score += hand.Count(card => card.IsMond) * 3;
		score += hand.Count(card => card.IsPagat) * 1;
		score += hand.Count(card => card.IsTrump && card.Rank is >= 15 and <= 20) * 0.5;
		score += hand.Count(card => card.IsKing) * 1.5;
		score += hand.Count(card => !card.IsTrump && card.Points == 4) * 0.5;

		return score;
	}

	private double Daring => _profile switch {
		BotProfile.Cautious => -1.5,
		BotProfile.Aggressive => 1.5,
		_ => 0
	};

	private static double ThresholdFor(Contract contract) => contract switch {
		Contract.Klop => double.MaxValue,       // only ever a fallback
		Contract.Three => 8,
		Contract.Two => 11,
		Contract.One => 13.5,
		Contract.SoloThree => 16,
		Contract.SoloTwo => 18,
		Contract.SoloOne => 20,
		Contract.SoloWithout => 23,
		_ => double.MaxValue                    // beggars and valats are judged by shape, not strength
	};

	/// <summary>A hand with nothing that can take a trick: no king, barely a trump, and those tiny.</summary>
	private static bool LooksLikeABeggar(IReadOnlyList<Card> hand) {
		if (hand.Any(card => card.IsKing)) {
			return false;
		}

		List<Card> trumps = hand.Where(card => card.IsTrump).ToList();
		return trumps.Count <= 3
			&& trumps.All(card => card.Rank <= 6)
			&& hand.Count(card => !card.IsTrump && card.Points >= 3) <= 1;
	}

	// --- calling a king ---

	public ValueTask<Suit> ChooseKingAsync(PlayerView view, CancellationToken cancellationToken = default) {
		// Call into the suit you are longest in and do not already hold the king of: you can lead it,
		// and the king comes home to your side.
		Suit best = view.LegalKingCalls
			.OrderByDescending(suit => view.Hand.Any(card => card.Suit == suit && card.IsKing) ? -1 : view.Hand.Count(card => card.Suit == suit))
			.First();

		return ValueTask.FromResult(best);
	}

	// --- talon ---

	/// <summary>The packets are face down, so there is nothing to reason about.</summary>
	public ValueTask<int> ChooseTalonPacketAsync(PlayerView view, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(_random.Next(view.TalonPacketCount));

	public ValueTask<IReadOnlyList<Card>> ChooseDiscardsAsync(PlayerView view, CancellationToken cancellationToken = default) {
		// Cards laid away count for the declarer, so the lay-away is the safest place for points.
		// Prefer a suit you can then be void in, and keep every trump you can.
		Dictionary<Suit, int> lengths = view.Hand
			.Where(card => !card.IsTrump)
			.GroupBy(card => card.Suit)
			.ToDictionary(group => group.Key, group => group.Count());

		List<Card> chosen = [.. view.LegalDiscards
			.OrderBy(card => card.IsTrump ? 1 : 0)
			.ThenByDescending(card => card.Points)
			.ThenBy(card => lengths.GetValueOrDefault(card.Suit, 99))
			.Take(view.DiscardCount)];

		return ValueTask.FromResult<IReadOnlyList<Card>>(chosen);
	}

	/// <summary>A barvni valat needs suits, not trumps, and almost no hand has them.</summary>
	public ValueTask<bool> ChooseColourValatUpgradeAsync(PlayerView view, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(false);

	// --- announcements ---

	public ValueTask<AnnouncementChoice> ChooseAnnouncementAsync(PlayerView view, CancellationToken cancellationToken = default) {
		int trumps = view.Hand.Count(card => card.IsTrump);
		int trulaHeld = view.Hand.Count(card => card.IsTrulaCard);
		int kingsHeld = view.Hand.Count(card => card.IsKing);
		int pagatBar = _profile == BotProfile.Aggressive ? 5 : 7;

		if (view.LegalAnnouncements.Contains(Bonus.PagatUltimo) && trumps >= pagatBar) {
			return ValueTask.FromResult(AnnouncementChoice.Announcing(Bonus.PagatUltimo));
		}

		if (view.LegalAnnouncements.Contains(Bonus.Trula) && trulaHeld == 3) {
			return ValueTask.FromResult(AnnouncementChoice.Announcing(Bonus.Trula));
		}

		if (view.LegalAnnouncements.Contains(Bonus.Kings) && kingsHeld >= 3) {
			return ValueTask.FromResult(AnnouncementChoice.Announcing(Bonus.Kings));
		}

		// Doubling is for hands that are strong and on the wrong side of the contract.
		if (!view.YouAreDeclaringSide && view.LegalKontras.Contains(KontraTarget.Game)
			&& Strength(view.Hand) + Daring >= 14) {

			return ValueTask.FromResult(AnnouncementChoice.Doubling(KontraTarget.Game));
		}

		return ValueTask.FromResult(AnnouncementChoice.Pass);
	}

	// --- playing ---

	public ValueTask<Card> ChooseCardAsync(PlayerView view, CancellationToken cancellationToken = default) {
		IReadOnlyList<Card> legal = view.LegalPlays;

		if (legal.Count == 1) {
			return ValueTask.FromResult(legal[0]);
		}

		ContractInfo info = Contracts.Info(view.Contract!.Value);

		return ValueTask.FromResult(info.UsesNegativePlayRules
			? ChooseForNegativeContract(view, info, legal)
			: ChooseForPositiveContract(view, info, legal));
	}

	/// <summary>Klop and berač: take nothing, and when you cannot avoid a trick, take it cheaply.</summary>
	private static Card ChooseForNegativeContract(PlayerView view, ContractInfo info, IReadOnlyList<Card> legal) {
		if (view.CurrentTrick.Count == 0) {
			// Lead something small out of your longest suit so the lead comes back to somebody else.
			return legal
				.OrderBy(card => card.IsTrump ? 1 : 0)
				.ThenBy(card => card.Points)
				.ThenBy(card => card.Rank)
				.First();
		}

		List<Card> losers = legal.Where(card => !WouldTakeTheTrick(view, info, card)).ToList();

		// If the trick is going elsewhere, throw the most expensive card you can be rid of.
		return losers.Count > 0
			? losers.OrderByDescending(card => card.Points).ThenByDescending(card => card.Rank).First()
			: legal.OrderBy(card => card.Points).ThenBy(card => card.Rank).First();
	}

	private Card ChooseForPositiveContract(PlayerView view, ContractInfo info, IReadOnlyList<Card> legal) {
		if (view.CurrentTrick.Count == 0) {
			return Lead(view, legal);
		}

		int winnerSeat = TrickRules.Winner(info, view.CurrentTrick);
		bool friendIsWinning = view.IsOnYourSide(winnerSeat);
		int pointsOnTable = view.CurrentTrick.Sum(played => played.Card.Points);

		if (friendIsWinning) {
			// Feed the trick: the points go to your side either way, so give them the fat cards.
			List<Card> safe = legal.Where(card => !WouldTakeTheTrick(view, info, card)).ToList();
			if (safe.Count > 0) {
				return safe.OrderByDescending(card => card.Points).ThenBy(card => card.Rank).First();
			}
		}

		List<Card> winners = legal.Where(card => WouldTakeTheTrick(view, info, card)).ToList();

		if (winners.Count > 0 && (pointsOnTable >= 5 || view.CompletedTricks.Count >= 9 || friendIsWinning)) {
			// Win it, but with the least valuable card that does the job.
			return winners.OrderBy(card => card.IsTrump ? card.Rank : 0).ThenBy(card => card.Points).First();
		}

		if (winners.Count > 0 && pointsOnTable >= 3) {
			return winners.OrderBy(card => card.Points).ThenBy(card => card.Rank).First();
		}

		// Nothing worth taking: hold on to your five-pointers.
		return legal.OrderBy(card => card.Points).ThenBy(card => card.Rank).First();
	}

	private Card Lead(PlayerView view, IReadOnlyList<Card> legal) {
		int trumps = view.Hand.Count(card => card.IsTrump);

		// Strong in trumps and playing the contract: pull them out of everybody else's hands.
		if (view.YouAreDeclaringSide && trumps >= 5) {
			List<Card> highTrumps = legal.Where(card => card.IsTrump && !card.IsPagat).ToList();
			if (highTrumps.Count > 0) {
				return highTrumps.OrderByDescending(card => card.Rank).First();
			}
		}

		// Looking for the partner: lead the suit whose king you called.
		if (view.Declarer == view.Seat && view.CalledKingSuit is Suit called && view.KnownPartner is null) {
			List<Card> inSuit = legal.Where(card => card.Suit == called).ToList();
			if (inSuit.Count > 0) {
				return inSuit.OrderByDescending(card => card.Rank).First();
			}
		}

		Suit longest = legal
			.Where(card => !card.IsTrump)
			.GroupBy(card => card.Suit)
			.OrderByDescending(group => group.Count())
			.Select(group => group.Key)
			.FirstOrDefault(Suit.Trump);

		List<Card> candidates = legal.Where(card => card.Suit == longest).ToList();
		if (candidates.Count == 0) {
			candidates = [.. legal];
		}

		return candidates.OrderBy(card => card.Points).ThenBy(card => card.Rank).First();
	}

	/// <summary>Would this card be winning the trick if it were played right now?</summary>
	private static bool WouldTakeTheTrick(PlayerView view, ContractInfo info, Card card) {
		List<TrickCard> trick = [.. view.CurrentTrick, new TrickCard(view.Seat, card)];
		return TrickRules.Winner(info, trick) == view.Seat;
	}
}
