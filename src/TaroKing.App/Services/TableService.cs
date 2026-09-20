using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace TaroKing.App.Services;

/// <summary>A table as the lobby lists it — never anything about the cards.</summary>
public sealed record TableSummary(
	string Id,
	string Name,
	TablePhase Phase,
	int Seated,
	int Seats,
	int Watching,
	int Round,
	int Rounds,
	bool MembersOnly,
	int MinimumRating,
	string Options);

/// <summary>
/// Every open table. Singleton, because a table belongs to the server rather than to any one
/// browser circuit: that is what lets four people share it and what lets any of them reload.
/// </summary>
public sealed class TableService : IDisposable {

	/// <summary>A table nobody has touched for this long is closed.</summary>
	public static readonly TimeSpan IdleLifetime = TimeSpan.FromHours(3);

	private readonly ConcurrentDictionary<string, OnlineTable> _tables = new(StringComparer.Ordinal);

	/// <summary>Raised when a table opens or closes, so the lobby can redraw.</summary>
	public event Action? LobbyChanged;

	public OnlineTable Open(TableOptions options, PlayerIdentity host) {
		ArgumentNullException.ThrowIfNull(options);

		OnlineTable table = new(NewId(), options, host);
		_tables[table.Id] = table;

		LobbyChanged?.Invoke();

		return table;
	}

	public OnlineTable? Find(string? id) =>
		string.IsNullOrWhiteSpace(id) ? null : _tables.GetValueOrDefault(id);

	/// <summary>Everything a lobby visitor may see: public tables, newest first.</summary>
	public IReadOnlyList<TableSummary> Lobby() => [.. _tables.Values
		.Where(table => table.Options.Visibility == TableVisibility.Public)
		.OrderByDescending(table => table.LastActivity)
		.Select(Summarise)];

	public static TableSummary Summarise(OnlineTable table) {
		ArgumentNullException.ThrowIfNull(table);

		return new TableSummary(
			Id: table.Id,
			Name: table.Options.Name,
			Phase: table.Phase,
			Seated: table.HumansSeated,
			Seats: table.Options.Seats,
			Watching: table.Watching,
			Round: Math.Max(table.HandNumber, 1),
			Rounds: table.Options.Rounds,
			MembersOnly: table.Options.MembersOnly,
			MinimumRating: table.Options.MinimumRating,
			Options: table.Options.Describe());
	}

	public IReadOnlyList<OnlineTable> All => [.. _tables.Values];

	public void Close(string id) {
		if (_tables.TryRemove(id, out OnlineTable? table)) {
			table.Dispose();
			LobbyChanged?.Invoke();
		}
	}

	/// <summary>Close tables that have finished or been abandoned.</summary>
	public int Sweep(DateTimeOffset now) {
		int closed = 0;

		foreach (OnlineTable table in _tables.Values) {
			bool empty = table.HumansSeated == 0 && table.Watching == 0;
			bool stale = now - table.LastActivity > IdleLifetime;

			if ((empty && stale) || (table.Phase == TablePhase.Finished && stale)) {
				Close(table.Id);
				closed++;
			}
		}

		return closed;
	}

	private static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant();

	public void Dispose() {
		foreach (OnlineTable table in _tables.Values) {
			table.Dispose();
		}

		_tables.Clear();
	}
}
