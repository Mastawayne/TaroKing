using TaroKing.Engine.Announcing;
using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;

namespace TaroKing.App.Services;

/// <summary>
/// Everything a person at a table can ask for, whichever kind of table it is. The action panel
/// speaks only this, so the same buttons drive a game against bots and a game against people —
/// and in both cases the call lands on a server that checks it rather than on the client.
/// </summary>
public interface ITableActions {

	/// <summary>The last thing this player was refused, or null.</summary>
	string? Notice { get; }

	string SeatName(int seat);

	/// <summary>Whether the "next hand" button belongs on screen at all.</summary>
	bool CanDealNext { get; }

	Task DealNextAsync();

	Task BidAsync(Contract contract);

	Task PassBidAsync();

	Task CallKingAsync(Suit suit);

	Task TakeTalonPacketAsync(int packet);

	Task DiscardAsync(IReadOnlyList<Card> cards);

	Task UpgradeAsync(bool upgrade);

	Task AnnounceAsync(Bonus bonus);

	Task KontraAsync(KontraTarget target);

	Task PassAnnouncementAsync();

	Task PlayCardAsync(Card card);
}

/// <summary>One seat at an online table, wired up for the person sitting in it.</summary>
public sealed class OnlineActions(OnlineTable table, PlayerIdentity player) : ITableActions {

	public string? Notice => table.NoticeFor(player);

	public string SeatName(int seat) => table.NameOf(seat);

	/// <summary>Online, the table deals on its own clock; nobody presses anything.</summary>
	public bool CanDealNext => false;

	public Task DealNextAsync() => Task.CompletedTask;

	public Task BidAsync(Contract contract) => table.BidAsync(player, contract);

	public Task PassBidAsync() => table.PassBidAsync(player);

	public Task CallKingAsync(Suit suit) => table.CallKingAsync(player, suit);

	public Task TakeTalonPacketAsync(int packet) => table.TakeTalonPacketAsync(player, packet);

	public Task DiscardAsync(IReadOnlyList<Card> cards) => table.DiscardAsync(player, cards);

	public Task UpgradeAsync(bool upgrade) => table.UpgradeAsync(player, upgrade);

	public Task AnnounceAsync(Bonus bonus) => table.AnnounceAsync(player, bonus);

	public Task KontraAsync(KontraTarget target) => table.KontraAsync(player, target);

	public Task PassAnnouncementAsync() => table.PassAnnouncementAsync(player);

	public Task PlayCardAsync(Card card) => table.PlayCardAsync(player, card);
}
