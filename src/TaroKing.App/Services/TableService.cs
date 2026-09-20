using System.Collections.Concurrent;
using System.Security.Cryptography;

using TaroKing.Data;

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
///
/// It also bounds the surface: a host may have two unfinished tables and open five in ten minutes,
/// and the server holds at most <see cref="MaxTables"/> in all. A script cannot open thousands.
/// </summary>
public sealed class TableService : IDisposable {

	/// <summary>A table nobody has touched for this long is closed.</summary>
	public static readonly TimeSpan IdleLifetime = TimeSpan.FromHours(3);

	public const int MaxTablesPerHost = 2;

	public const int MaxOpensPerWindow = 5;

	public static readonly TimeSpan OpenWindow = TimeSpan.FromMinutes(10);

	private readonly ConcurrentDictionary<string, OnlineTable> _tables = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _opens = new(StringComparer.Ordinal);
	private readonly ITableJournal? _journal;
	private readonly ChatFilter _filter;
	private readonly ServerMetrics? _metrics;

	public TableService() : this(null, null, null, 200) {
	}

	public TableService(ITableJournal? journal, ChatFilter? filter, ServerMetrics? metrics, int maxTables = 200) {
		_journal = journal;
		_filter = filter ?? ChatFilter.None;
		_metrics = metrics;
		MaxTables = maxTables;

		if (_metrics is not null) {
			_metrics.Live = () => {
				OnlineTable[] all = [.. _tables.Values];

				return (all.Length, all.Sum(table => table.Online), all.Count(table => table.Phase == TablePhase.Playing));
			};
		}
	}

	/// <summary>Raised when a table opens or closes, so the lobby can redraw.</summary>
	public event Action? LobbyChanged;

	public int MaxTables { get; }

	/// <summary>Set when the host is stopping: no new tables, no new deals.</summary>
	public bool IsShuttingDown { get; private set; }

	/// <summary>Open a table. Returns null and a reason when the host has hit a limit.</summary>
	public OnlineTable? TryOpen(TableOptions options, PlayerIdentity host, IReadOnlySet<string>? hostBlocks, out string? refusal) {
		ArgumentNullException.ThrowIfNull(options);

		refusal = null;

		if (IsShuttingDown) {
			refusal = "Strežnik se posodablja — poskusi čez minuto.";
			return null;
		}

		if (!host.IsKnown) {
			refusal = "Najprej se predstavi.";
			return null;
		}

		if (host.IsBanned) {
			refusal = "Tvoj račun ima prepoved igranja.";
			return null;
		}

		if (_tables.Count >= MaxTables) {
			refusal = "Strežnik ima trenutno odprtih preveč miz.";
			return null;
		}

		int mine = _tables.Values.Count(table => table.Host.Id == host.Id && table.Phase != TablePhase.Finished);
		if (mine >= MaxTablesPerHost) {
			refusal = $"Odprti imaš že {MaxTablesPerHost} mizi — najprej odigraj ali zapri eno.";
			return null;
		}

		DateTimeOffset now = DateTimeOffset.UtcNow;
		Queue<DateTimeOffset> recent = _opens.GetOrAdd(host.Id, _ => new Queue<DateTimeOffset>());
		lock (recent) {
			while (recent.Count > 0 && now - recent.Peek() > OpenWindow) {
				recent.Dequeue();
			}

			if (recent.Count >= MaxOpensPerWindow) {
				refusal = "Preveč miz v kratkem času — počakaj nekaj minut.";
				return null;
			}

			recent.Enqueue(now);
		}

		OnlineTable table = new(NewId(), options, host, _journal, hostBlocks, _filter);
		_tables[table.Id] = table;

		_journal?.TableChanged(table);
		_metrics?.TableOpened();
		LobbyChanged?.Invoke();

		return table;
	}

	/// <summary>Open a table without limits — for tests and tools. The lobby goes through <see cref="TryOpen"/>.</summary>
	public OnlineTable Open(TableOptions options, PlayerIdentity host) {
		ArgumentNullException.ThrowIfNull(options);

		OnlineTable table = new(NewId(), options, host, _journal, null, _filter);
		_tables[table.Id] = table;

		_journal?.TableChanged(table);
		_metrics?.TableOpened();
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
			_journal?.TableClosed(id);
			LobbyChanged?.Invoke();
		}
	}

	/// <summary>Close tables that have finished or been abandoned.</summary>
	public int Sweep(DateTimeOffset now) {
		int closed = 0;

		foreach (OnlineTable table in _tables.Values) {
			bool empty = table.HumansSeated == 0 && table.Watching == 0;
			bool stale = now - table.LastActivity > IdleLifetime;
			bool done = table.Phase == TablePhase.Finished && (table.Archived || table.Faulted);

			if ((empty && stale) || (done && stale) || (table.Faulted && table.Online == 0)) {
				Close(table.Id);
				closed++;
			}
		}

		return closed;
	}

	/// <summary>
	/// Put back every table the journal knows about. Called once, before the first heartbeat.
	/// A finished table that was never archived is handed to the archiver again; a table whose
	/// journal cannot be read is dropped with a log line rather than left to break the heartbeat.
	/// </summary>
	public async Task<int> RestoreAsync(LiveTableStore store, MatchArchiver archiver, ILogger log, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(store);

		IReadOnlyList<LiveTableSnapshot> saved = await store.LoadAllAsync(cancellationToken);
		int restored = 0;

		foreach (LiveTableSnapshot snapshot in saved) {
			OnlineTable? table;
			try {
				table = OnlineTable.Restore(snapshot, _journal, _filter);
			} catch (Exception error) {
				log.LogError(error, "Could not restore table {TableId}; dropping its journal.", snapshot.Table.Id);
				table = null;
			}

			if (table is null) {
				await store.DeleteAsync(snapshot.Table.Id, cancellationToken);
				continue;
			}

			_tables[table.Id] = table;
			restored++;

			if (table.Phase == TablePhase.Finished) {
				table.Archived = archiver.Enqueue(table);
			} else {
				_journal?.TableChanged(table);
			}
		}

		if (restored > 0) {
			LobbyChanged?.Invoke();
		}

		return restored;
	}

	/// <summary>The host is stopping: tell every table, deal nothing new.</summary>
	public void BeginShutdown() {
		IsShuttingDown = true;

		foreach (OnlineTable table in _tables.Values) {
			table.SuspendDeals();
		}
	}

	private static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant();

	public void Dispose() {
		foreach (OnlineTable table in _tables.Values) {
			table.Dispose();
		}

		_tables.Clear();
	}
}
