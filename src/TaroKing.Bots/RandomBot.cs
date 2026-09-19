using TaroKing.Engine.Announcing;
using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;
using TaroKing.Engine.Randomness;
using TaroKing.Engine.Session;

namespace TaroKing.Bots;

/// <summary>
/// Picks uniformly among the legal options. It plays badly on purpose: it is the baseline every
/// other agent has to beat, and the fuzz partner that walks the engine into odd corners.
/// </summary>
public sealed class RandomBot : IPlayerAgent {

	private readonly Pcg32 _random;

	public RandomBot(int seed, string? name = null) {
		_random = new Pcg32(seed);
		Name = name ?? $"Random({seed})";
	}

	public string Name { get; }

	public ValueTask<Contract?> ChooseBidAsync(PlayerView view, CancellationToken cancellationToken = default) {
		if (view.LegalBids.Count == 0) {
			return ValueTask.FromResult<Contract?>(null);
		}

		// Forehand's privilege leaves no way out: something has to be named.
		if (!view.CanPassBid) {
			return ValueTask.FromResult<Contract?>(view.LegalBids[_random.Next(view.LegalBids.Count)]);
		}

		return ValueTask.FromResult<Contract?>(_random.Next(4) == 0
			? view.LegalBids[_random.Next(view.LegalBids.Count)]
			: null);
	}

	public ValueTask<Suit> ChooseKingAsync(PlayerView view, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(view.LegalKingCalls[_random.Next(view.LegalKingCalls.Count)]);

	public ValueTask<int> ChooseTalonPacketAsync(PlayerView view, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(_random.Next(view.TalonPacketCount));

	public ValueTask<IReadOnlyList<Card>> ChooseDiscardsAsync(PlayerView view, CancellationToken cancellationToken = default) {
		List<Card> pool = [.. view.LegalDiscards];
		_random.Shuffle(pool);

		return ValueTask.FromResult<IReadOnlyList<Card>>(pool.Take(view.DiscardCount).ToList());
	}

	public ValueTask<bool> ChooseColourValatUpgradeAsync(PlayerView view, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(_random.Next(20) == 0);

	public ValueTask<AnnouncementChoice> ChooseAnnouncementAsync(PlayerView view, CancellationToken cancellationToken = default) {
		if (view.LegalAnnouncements.Count > 0 && _random.Next(6) == 0) {
			return ValueTask.FromResult(
				AnnouncementChoice.Announcing(view.LegalAnnouncements[_random.Next(view.LegalAnnouncements.Count)]));
		}

		if (view.LegalKontras.Count > 0 && _random.Next(8) == 0) {
			return ValueTask.FromResult(
				AnnouncementChoice.Doubling(view.LegalKontras[_random.Next(view.LegalKontras.Count)]));
		}

		return ValueTask.FromResult(AnnouncementChoice.Pass);
	}

	public ValueTask<Card> ChooseCardAsync(PlayerView view, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(view.LegalPlays[_random.Next(view.LegalPlays.Count)]);
}
