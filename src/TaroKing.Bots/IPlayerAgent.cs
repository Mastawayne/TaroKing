using TaroKing.Engine.Announcing;
using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;
using TaroKing.Engine.Session;

namespace TaroKing.Bots;

/// <summary>One thing a player can say during the announcement round.</summary>
/// <param name="Bonus">A bonus to announce for your own side.</param>
/// <param name="Kontra">Something belonging to the other side to double.</param>
public readonly record struct AnnouncementChoice(Bonus? Bonus, KontraTarget? Kontra) {

	public bool IsPass => Bonus is null && Kontra is null;

	public static AnnouncementChoice Pass => default;

	public static AnnouncementChoice Announcing(Bonus bonus) => new(bonus, null);

	public static AnnouncementChoice Doubling(KontraTarget target) => new(null, target);
}

/// <summary>
/// A player the table can ask for decisions. A human seat is driven by the UI and a bot by one of
/// these, and neither is trusted: every answer goes back through the engine, which rejects anything
/// illegal. An agent only ever sees a <see cref="PlayerView"/>, so a bot cannot cheat by looking at
/// cards it should not have — it plays from exactly what a person in that seat would know.
/// </summary>
public interface IPlayerAgent {

	string Name { get; }

	/// <summary>A contract to bid, or null to pass.</summary>
	ValueTask<Contract?> ChooseBidAsync(PlayerView view, CancellationToken cancellationToken = default);

	ValueTask<Suit> ChooseKingAsync(PlayerView view, CancellationToken cancellationToken = default);

	ValueTask<int> ChooseTalonPacketAsync(PlayerView view, CancellationToken cancellationToken = default);

	ValueTask<IReadOnlyList<Card>> ChooseDiscardsAsync(PlayerView view, CancellationToken cancellationToken = default);

	/// <summary>True to lift a solo to a barvni valat after seeing the talon.</summary>
	ValueTask<bool> ChooseColourValatUpgradeAsync(PlayerView view, CancellationToken cancellationToken = default);

	ValueTask<AnnouncementChoice> ChooseAnnouncementAsync(PlayerView view, CancellationToken cancellationToken = default);

	ValueTask<Card> ChooseCardAsync(PlayerView view, CancellationToken cancellationToken = default);
}
