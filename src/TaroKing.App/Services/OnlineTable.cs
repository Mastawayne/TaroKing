using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

using TaroKing.Bots;
using TaroKing.Data;
using TaroKing.Engine;
using TaroKing.Engine.Announcing;
using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;
using TaroKing.Engine.Scoring;
using TaroKing.Engine.Session;

namespace TaroKing.App.Services;

public enum TablePhase {
	/// <summary>Waiting for enough people to sit down.</summary>
	Waiting = 0,

	Playing = 1,

	/// <summary>All the rounds have been played.</summary>
	Finished = 2
}

/// <summary>One line of table talk. Seat -1 is the table itself. <paramref name="UserId"/> lets a reader hide people they blocked.</summary>
public sealed record ChatLine(DateTimeOffset At, int Seat, string Who, string Text, bool FromTable = false, string? UserId = null);

/// <summary>
/// One chair as a page may see it: who sits there, whether they are connected, whether a bot is
/// playing it. Immutable — it is part of a <see cref="TableSnapshot"/>, never the live state.
/// </summary>
public sealed record TableSeat(int Index, PlayerIdentity? Player, int Connections, bool BotIsPlaying, bool Abandoned, double Reserve) {

	public bool IsTaken => Player is not null;

	public string Name => Player?.Name ?? $"Bot {Index + 1}";
}

/// <summary>
/// Everything about a table that anybody outside it may read, built inside the table's gate and
/// published as one reference. A page reads a snapshot and only a snapshot, so it can never see
/// a list being modified or a hand half-way through a move.
/// </summary>
public sealed record TableSnapshot(
	TablePhase Phase,
	int HandNumber,
	IReadOnlyList<TableSeat> Seats,
	IReadOnlyList<ChatLine> Chat,
	IReadOnlyList<HandRecord> Records,
	IReadOnlyList<PlayerView?> Views,
	PlayerView? SpectatorView,
	int Watching,
	int? TurnSeat,
	DateTimeOffset? TurnStartedAt,
	DateTimeOffset LastActivity);

/// <summary>Where a table writes itself down as it goes, so a restart can put it back.</summary>
public interface ITableJournal {

	/// <summary>The header changed: seats, phase, chat, options.</summary>
	void TableChanged(OnlineTable table);

	/// <summary>Events were appended to a hand.</summary>
	void EventsAppended(string tableId, int handNumber, int firstOrdinal, IReadOnlyList<GameEvent> events);

	/// <summary>The table is gone for good.</summary>
	void TableClosed(string tableId);
}

/// <summary>
/// One online table: four seats, one authoritative hand, and everything anybody is allowed to know
/// about it. Nothing here trusts a client. A move arrives with the identity that sent it, is
/// matched to the seat that identity actually owns, and then goes through the same engine calls a
/// bot's move would — so a forged seat number or an illegal card is refused, not obeyed.
///
/// Every mutation, from a click to a heartbeat, takes the one gate; every read goes through the
/// snapshot published on the way out. That is what keeps four circuits and a heartbeat thread
/// from ever seeing the same list from both sides.
/// </summary>
public sealed class OnlineTable : IDisposable {

	/// <summary>How long a seat is held for somebody who drops before a bot takes over.</summary>
	public static readonly TimeSpan ReconnectGrace = TimeSpan.FromSeconds(60);

	/// <summary>How long a bot waits before answering, so the table does not blur past.</summary>
	public static readonly TimeSpan BotPace = TimeSpan.FromMilliseconds(900);

	/// <summary>The pause between a finished hand and the next deal.</summary>
	public static readonly TimeSpan BetweenHands = TimeSpan.FromSeconds(6);

	/// <summary>Chat lines one person may send within <see cref="ChatWindow"/>.</summary>
	public const int ChatBurst = 5;

	public static readonly TimeSpan ChatWindow = TimeSpan.FromSeconds(10);

	/// <summary>After this many heartbeats in a row that threw, a bot that is always legal takes the seat.</summary>
	public const int FailuresBeforeRandomBot = 3;

	/// <summary>After this many, the table is closed rather than left to spin.</summary>
	public const int FailuresBeforeClosing = 10;

	private const int MaxChatLines = 80;

	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly Chair[] _chairs;
	private readonly IPlayerAgent[] _agents = new IPlayerAgent[TarokConstants.PlayerCount];
	private readonly List<HandRecord> _records = [];
	private readonly List<ChatLine> _chat = [];
	private readonly ConcurrentDictionary<string, string> _notices = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Queue<DateTimeOffset>> _chatRate = new(StringComparer.Ordinal);
	private readonly ITableJournal? _journal;
	private readonly IReadOnlySet<string> _hostBlocks;
	private readonly ChatFilter? _filter;

	private TableSnapshot _snapshot;
	private DateTimeOffset? _turnStartedAt;
	private int? _turnSeat;
	private DateTimeOffset? _dealNextAt;
	private bool _radlcApplied;
	private bool _handRecorded;
	private int _journaledEvents;
	private int _failures;
	private int _watching;
	private bool _disposed;

	/// <summary>The live, mutable chair. Only ever touched inside the gate.</summary>
	private sealed class Chair(int index) {
		public int Index { get; } = index;
		public PlayerIdentity? Player { get; set; }
		public int Connections { get; set; }
		public DateTimeOffset? AwaySince { get; set; }
		public bool BotIsPlaying { get; set; }
		public bool Abandoned { get; set; }
		public double Reserve { get; set; }
		public bool IsTaken => Player is not null;
		public string Name => Player?.Name ?? $"Bot {Index + 1}";

		public TableSeat Freeze() => new(Index, Player, Connections, BotIsPlaying, Abandoned, Reserve);
	}

	public OnlineTable(string id, TableOptions options, PlayerIdentity host, ITableJournal? journal = null, IReadOnlySet<string>? hostBlocks = null, ChatFilter? filter = null) {
		ArgumentException.ThrowIfNullOrWhiteSpace(id);
		ArgumentNullException.ThrowIfNull(options);

		Id = id;
		Options = options.Clamped();
		Host = host;
		OpenedAt = DateTimeOffset.UtcNow;
		LastActivity = OpenedAt;
		_journal = journal;
		_hostBlocks = hostBlocks ?? new HashSet<string>();
		_filter = filter;

		_chairs = [.. Enumerable.Range(0, TarokConstants.PlayerCount).Select(seat => new Chair(seat) {
			Reserve = Options.ReserveSeconds
		})];

		for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
			_agents[seat] = new HeuristicBot(BitConverter.ToInt32(RandomNumberGenerator.GetBytes(sizeof(int))), BotProfile.Normal);
		}

		Table($"Miza »{Options.Name}« je odprta: {Options.Describe()}.");
		_snapshot = Build();
	}

	/// <summary>Raised whenever anything changes that a watching page would want to redraw.</summary>
	public event Action? Changed;

	public string Id { get; }

	public TableOptions Options { get; }

	public PlayerIdentity Host { get; }

	public DateTimeOffset OpenedAt { get; private set; }

	public DateTimeOffset LastActivity { get; private set; }

	/// <summary>When the first hand was dealt. Null while the table is still waiting.</summary>
	public DateTimeOffset? StartedAt { get; private set; }

	/// <summary>Set by whoever queues the finished table for the archive, so it is queued once.</summary>
	public bool Archived { get; set; }

	/// <summary>The table threw too often and closed itself; nothing of it is worth archiving.</summary>
	public bool Faulted { get; private set; }

	/// <summary>The server is going down: finish the hand, deal no new one.</summary>
	public bool DealsSuspended { get; private set; }

	public TablePhase Phase { get; private set; } = TablePhase.Waiting;

	/// <summary>The live hand. Read it only from the heartbeat or a test; pages use <see cref="Snapshot"/>.</summary>
	public HandState? Hand { get; private set; }

	public ScoreSheet Sheet { get; } = new();

	/// <summary>What the outside world reads. Replaced as a whole after every change.</summary>
	public TableSnapshot Snapshot => Volatile.Read(ref _snapshot);

	public IReadOnlyList<TableSeat> Seats => Snapshot.Seats;

	public IReadOnlyList<HandRecord> Records => Snapshot.Records;

	public IReadOnlyList<ChatLine> Chat => Snapshot.Chat;

	public int HandsPlayed => Snapshot.Records.Count;

	public int HandNumber => Snapshot.HandNumber;

	public int HumansSeated => Snapshot.Seats.Count(seat => seat.IsTaken);

	public int Watching => Snapshot.Watching;

	/// <summary>People with a page open on this table — seated and connected, or watching.</summary>
	public int Online => Snapshot.Seats.Count(seat => seat.IsTaken && seat.Connections > 0) + Snapshot.Watching;

	public bool IsFull => Snapshot.Seats.All(seat => seat.IsTaken);

	public string NameOf(int seat) =>
		seat >= 0 && seat < TarokConstants.PlayerCount ? Snapshot.Seats[seat].Name : "miza";

	// --- sitting down and standing up ---

	/// <summary>Take a seat. Returns null on success, or the reason it was refused.</summary>
	public string? Sit(PlayerIdentity player, int seat) {
		if (!player.IsKnown) {
			return "Najprej se predstavi.";
		}

		if (seat < 0 || seat >= _chairs.Length) {
			return "Takega mesta ni.";
		}

		if (Options.MembersOnly && !player.IsMember) {
			return "Ta miza je samo za prijavljene.";
		}

		if (Options.MinimumRating > 0 && player.Rating < Options.MinimumRating) {
			return $"Za to mizo je treba imeti vsaj {Options.MinimumRating} točk.";
		}

		if (player.IsBanned) {
			return "Tvoj račun ima prepoved igranja.";
		}

		if (player.UserId is string userId && _hostBlocks.Contains(userId)) {
			return "Gostitelj te je blokiral.";
		}

		return Locked(() => {
			if (Phase == TablePhase.Finished) {
				return "Miza je odigrana.";
			}

			if (SeatOfUnlocked(player) is int already) {
				return already == seat ? null : "Že sediš za to mizo.";
			}

			Chair chair = _chairs[seat];
			if (chair.IsTaken) {
				return "Mesto je zasedeno.";
			}

			chair.Player = player;

			// They are looking at the table right now, so they are present, not away.
			chair.Connections = 1;
			chair.AwaySince = null;
			chair.BotIsPlaying = false;
			chair.Abandoned = false;
			chair.Reserve = Options.ReserveSeconds;
			_watching = Math.Max(0, _watching - 1);

			Table($"{player.Name} sede na mesto {seat + 1}.");
			Touch();
			HeaderChanged();

			return null;
		});
	}

	/// <summary>
	/// Leave the table. Before the first deal the seat is simply freed. During play the seat stays
	/// theirs — a bot plays it, they may come back to it — and the walk-out is held against them.
	/// </summary>
	public void Stand(PlayerIdentity player) => Locked(() => {
		if (SeatOfUnlocked(player) is not int seat) {
			return;
		}

		Chair chair = _chairs[seat];

		if (Phase == TablePhase.Playing) {
			chair.BotIsPlaying = true;
			chair.Abandoned = true;
			chair.Connections = 0;
			chair.AwaySince = DateTimeOffset.UtcNow;
			_watching++;

			Table($"{player.Name} je vstal sredi igre — mesto igra bot.");
		} else {
			chair.Player = null;
			chair.Connections = 0;
			chair.AwaySince = null;
			chair.BotIsPlaying = false;
			chair.Abandoned = false;
			_watching++;

			Table($"{player.Name} je vstal od mize.");
		}

		Touch();
		HeaderChanged();
	});

	/// <summary>Sit back down in your own chair after standing up mid-game. The walk-out still counts.</summary>
	public void Rejoin(PlayerIdentity player) => Locked(() => {
		if (SeatOfUnlocked(player) is not int seat || !_chairs[seat].BotIsPlaying) {
			return;
		}

		Chair chair = _chairs[seat];
		chair.BotIsPlaying = false;
		chair.AwaySince = null;
		chair.Connections = Math.Max(1, chair.Connections);
		_watching = Math.Max(0, _watching - 1);

		Table($"{chair.Name} je spet za mizo.");
		Touch();
		HeaderChanged();
	});

	/// <summary>A page opened on this table.</summary>
	public void Attach(PlayerIdentity player) => Locked(() => {
		if (SeatOfUnlocked(player) is int seat) {
			Chair chair = _chairs[seat];
			chair.Connections++;
			chair.AwaySince = null;

			if (chair.BotIsPlaying) {
				chair.BotIsPlaying = false;
				_watching = Math.Max(0, _watching - 1);
				Table($"{chair.Name} je spet za mizo.");
			}
		} else {
			_watching++;
		}

		Touch();
	});

	/// <summary>A page closed. The seat is held for <see cref="ReconnectGrace"/> before a bot takes it.</summary>
	public void Detach(PlayerIdentity player) => Locked(() => {
		if (SeatOfUnlocked(player) is int seat) {
			Chair chair = _chairs[seat];
			chair.Connections = Math.Max(0, chair.Connections - 1);

			if (chair.Connections == 0) {
				chair.AwaySince = DateTimeOffset.UtcNow;
			}
		} else {
			_watching = Math.Max(0, _watching - 1);
		}
	});

	public int? SeatOf(PlayerIdentity player) {
		if (!player.IsKnown) {
			return null;
		}

		foreach (TableSeat seat in Snapshot.Seats) {
			if (seat.Player?.Id == player.Id) {
				return seat.Index;
			}
		}

		return null;
	}

	private int? SeatOfUnlocked(PlayerIdentity player) {
		if (!player.IsKnown) {
			return null;
		}

		foreach (Chair chair in _chairs) {
			if (chair.Player?.Id == player.Id) {
				return chair.Index;
			}
		}

		return null;
	}

	/// <summary>What this person is allowed to see. A watcher gets the table, never a hand.</summary>
	public PlayerView? ViewFor(PlayerIdentity player) {
		TableSnapshot snapshot = Snapshot;

		return SeatOf(player) is int seat ? snapshot.Views[seat] : snapshot.SpectatorView;
	}

	/// <summary>Seconds this seat has before the clock runs out, as of now.</summary>
	public double ClockFor(int seat, DateTimeOffset now) {
		TableSnapshot snapshot = Snapshot;

		if (seat < 0 || seat >= snapshot.Seats.Count) {
			return 0;
		}

		double budget = Options.SecondsPerMove + snapshot.Seats[seat].Reserve;

		return snapshot.TurnSeat == seat && snapshot.TurnStartedAt is DateTimeOffset since
			? Math.Max(0, budget - (now - since).TotalSeconds)
			: budget;
	}

	// --- what a player may do ---

	public Task BidAsync(PlayerIdentity player, Contract contract) =>
		ActAsync(player, (hand, seat) => hand.PlaceBid(seat, contract));

	public Task PassBidAsync(PlayerIdentity player) =>
		ActAsync(player, (hand, seat) => hand.PassBid(seat));

	public Task CallKingAsync(PlayerIdentity player, Suit suit) =>
		ActAsync(player, (hand, seat) => hand.CallKing(seat, suit));

	public Task TakeTalonPacketAsync(PlayerIdentity player, int packet) =>
		ActAsync(player, (hand, seat) => hand.TakeTalonPacket(seat, packet));

	public Task DiscardAsync(PlayerIdentity player, IReadOnlyList<Card> cards) =>
		ActAsync(player, (hand, seat) => hand.Discard(seat, cards));

	public Task UpgradeAsync(PlayerIdentity player, bool upgrade) =>
		ActAsync(player, (hand, seat) => {
			if (upgrade) {
				hand.UpgradeToColourValat(seat);
			} else {
				hand.KeepContract(seat);
			}
		});

	public Task AnnounceAsync(PlayerIdentity player, Bonus bonus) =>
		ActAsync(player, (hand, seat) => hand.Announce(seat, bonus));

	public Task KontraAsync(PlayerIdentity player, KontraTarget target) =>
		ActAsync(player, (hand, seat) => hand.Kontra(seat, target));

	public Task PassAnnouncementAsync(PlayerIdentity player) =>
		ActAsync(player, (hand, seat) => hand.PassAnnouncement(seat));

	public Task PlayCardAsync(PlayerIdentity player, Card card) =>
		ActAsync(player, (hand, seat) => hand.PlayCard(seat, card));

	/// <summary>The last thing the table refused, per player, so one person's mistake is their own.</summary>
	public string? NoticeFor(PlayerIdentity player) =>
		player.IsKnown && _notices.TryGetValue(player.Id, out string? notice) ? notice : null;

	/// <summary>Say something. Muted players, floods and the word list are all handled here, never in the page.</summary>
	public void Say(PlayerIdentity player, string text) {
		if (string.IsNullOrWhiteSpace(text) || !player.IsKnown) {
			return;
		}

		if (player.IsMuted) {
			Refuse(player, "Moderator ti je začasno odvzel besedo.");
			Notify();
			return;
		}

		string trimmed = text.Trim();
		trimmed = trimmed[..Math.Min(trimmed.Length, 200)];
		trimmed = _filter?.Clean(trimmed) ?? trimmed;

		Locked(() => {
			DateTimeOffset now = DateTimeOffset.UtcNow;

			if (!_chatRate.TryGetValue(player.Id, out Queue<DateTimeOffset>? recent)) {
				recent = new Queue<DateTimeOffset>();
				_chatRate[player.Id] = recent;
			}

			while (recent.Count > 0 && now - recent.Peek() > ChatWindow) {
				recent.Dequeue();
			}

			if (recent.Count >= ChatBurst) {
				Refuse(player, "Počasi — preveč sporočil naenkrat.");
				return;
			}

			recent.Enqueue(now);
			Add(new ChatLine(now, SeatOfUnlocked(player) ?? -1, player.Name, trimmed, UserId: player.UserId));
			Touch();
			HeaderChanged();
		});

		Notify();
	}

	/// <summary>The server is shutting down: say so, finish what is on the table, deal nothing new.</summary>
	public void SuspendDeals() {
		Locked(() => {
			if (DealsSuspended) {
				return;
			}

			DealsSuspended = true;
			Table("Strežnik se posodablja — igra se nadaljuje čez minuto.");
		});

		Notify();
	}

	// --- the heartbeat ---

	/// <summary>
	/// One beat of the table's own clock: start when there are enough players, hand an abandoned
	/// seat to a bot, let a bot move, take a move away from somebody whose clock ran out, record a
	/// finished hand and deal the next.
	/// </summary>
	public async Task TickAsync(DateTimeOffset now) {
		if (_disposed || !await _gate.WaitAsync(TimeSpan.Zero)) {
			return;
		}

		bool changed = false;

		try {
			changed |= HandSeatsToBots(now);
			changed |= StartIfReady(now);
			changed |= DealIfDue(now);
			ApplyRadlc();

			if (Phase == TablePhase.Playing && Hand is HandState hand && hand.CurrentSeat is int seat) {
				WatchTheClock(seat, now);

				bool botsTurn = IsBotPlayingUnlocked(seat);
				bool outOfTime = !botsTurn && ClockUnlocked(seat, now) <= 0;

				if (outOfTime) {
					Table($"{_chairs[seat].Name} je porabil čas — potezo odigra bot.");
					_chairs[seat].Reserve = 0;
				}

				if ((botsTurn && now - _turnStartedAt >= BotPace) || outOfTime) {
					await BotTable.ActAsync(hand, _agents[seat], seat);
					AfterMove(seat, now);
					changed = true;
				}
			}

			changed |= RecordIfFinished(now);
			_failures = 0;
		} catch (Exception error) when (error is InvalidOperationException or ArgumentException) {
			changed = true;
			_failures++;

			if (_failures == FailuresBeforeRandomBot && Hand?.CurrentSeat is int stuck) {
				// A bot that keeps proposing the same illegal move gets replaced by one that cannot.
				_agents[stuck] = new RandomBot(BitConverter.ToInt32(RandomNumberGenerator.GetBytes(sizeof(int))));
				Table($"Bot na mestu {stuck + 1} se je zataknil — zamenjal ga je drug.");
			} else if (_failures >= FailuresBeforeClosing) {
				Faulted = true;
				Phase = TablePhase.Finished;
				Table("Miza se je pokvarila in se zapira. Oprostite.");
			} else if (_failures == 1) {
				Table($"Napaka za mizo: {error.Message}");
			}
		} finally {
			Publish();
			_gate.Release();
		}

		if (changed) {
			Notify();
		}
	}

	// --- the machinery ---

	/// <summary>Run a mutation inside the gate and publish afterwards.</summary>
	private void Locked(Action action) {
		Locked(() => {
			action();
			return (string?)null;
		});
	}

	private string? Locked(Func<string?> action) {
		if (_disposed || !_gate.Wait(TimeSpan.FromSeconds(5))) {
			return "Miza je zasedena, poskusi znova.";
		}

		try {
			return action();
		} finally {
			Publish();
			_gate.Release();
		}
	}

	private async Task ActAsync(PlayerIdentity player, Action<HandState, int> action) {
		if (_disposed || !await _gate.WaitAsync(TimeSpan.FromSeconds(5))) {
			Refuse(player, "Miza je zasedena, poskusi znova.");
			Notify();
			return;
		}

		try {
			Touch();

			if (player.IsKnown) {
				_notices.TryRemove(player.Id, out _);
			}

			if (SeatOfUnlocked(player) is not int seat) {
				Refuse(player, "Za to mizo samo gledaš.");
				return;
			}

			if (Hand is not HandState hand || Phase != TablePhase.Playing) {
				Refuse(player, "Trenutno ni partije.");
				return;
			}

			if (hand.CurrentSeat != seat) {
				Refuse(player, "Nisi na potezi.");
				return;
			}

			try {
				action(hand, seat);
			} catch (Exception error) when (error is InvalidOperationException or ArgumentException) {
				Refuse(player, error.Message);
				return;
			}

			AfterMove(seat, DateTimeOffset.UtcNow);
			ApplyRadlc();
		} finally {
			Publish();
			_gate.Release();
			Notify();
		}
	}

	private void Refuse(PlayerIdentity player, string reason) {
		if (player.IsKnown) {
			_notices[player.Id] = reason;
		}
	}

	/// <summary>Fischer clock: a move gives the increment back and costs whatever it took.</summary>
	private void AfterMove(int seat, DateTimeOffset now) {
		if (_turnSeat == seat && _turnStartedAt is DateTimeOffset since && !IsBotPlayingUnlocked(seat)) {
			double spent = (now - since).TotalSeconds;
			double reserve = _chairs[seat].Reserve + Options.SecondsPerMove - spent;

			_chairs[seat].Reserve = Math.Clamp(reserve, 0, Options.ReserveSeconds);
		}

		_turnSeat = null;
		_turnStartedAt = null;
	}

	private void WatchTheClock(int seat, DateTimeOffset now) {
		if (_turnSeat != seat) {
			_turnSeat = seat;
			_turnStartedAt = now;
		}
	}

	private double ClockUnlocked(int seat, DateTimeOffset now) {
		double budget = Options.SecondsPerMove + _chairs[seat].Reserve;

		return _turnSeat == seat && _turnStartedAt is DateTimeOffset since
			? Math.Max(0, budget - (now - since).TotalSeconds)
			: budget;
	}

	/// <summary>A seat nobody is connected to goes to a bot once the grace period is up.</summary>
	private bool HandSeatsToBots(DateTimeOffset now) {
		bool changed = false;

		foreach (Chair chair in _chairs) {
			bool abandoned = chair.IsTaken
				&& chair.Connections == 0
				&& chair.AwaySince is DateTimeOffset since
				&& now - since > ReconnectGrace;

			if (abandoned && !chair.BotIsPlaying) {
				chair.BotIsPlaying = true;
				Table($"{chair.Name} se ni vrnil — mesto igra bot.");
				changed = true;
			}
		}

		return changed;
	}

	/// <summary>A seat is played by a bot when it is empty and filled, or its owner walked off.</summary>
	public bool IsBotPlaying(int seat) {
		TableSeat chair = Snapshot.Seats[seat];

		return chair.BotIsPlaying || (!chair.IsTaken && Options.FillWithBots);
	}

	private bool IsBotPlayingUnlocked(int seat) =>
		_chairs[seat].BotIsPlaying || (!_chairs[seat].IsTaken && Options.FillWithBots);

	private bool StartIfReady(DateTimeOffset now) {
		if (Phase != TablePhase.Waiting || DealsSuspended) {
			return false;
		}

		bool everySeatAccountedFor = Options.FillWithBots || _chairs.All(chair => chair.IsTaken);
		if (!everySeatAccountedFor || !_chairs.Any(chair => chair.IsTaken)) {
			return false;
		}

		Phase = TablePhase.Playing;
		StartedAt = now;
		Table("Začenjamo.");
		Deal();

		return true;
	}

	private bool DealIfDue(DateTimeOffset now) {
		if (Phase != TablePhase.Playing || DealsSuspended || _dealNextAt is not DateTimeOffset due || now < due) {
			return false;
		}

		_dealNextAt = null;
		Deal();

		return true;
	}

	/// <summary>
	/// Deal from a seed nobody can guess. The engine's PRNG stays deterministic — the seed is what
	/// makes the hand reproducible for a replay — but it comes from the system's cryptographic
	/// source, so no player can work out the shuffle ahead of time.
	/// </summary>
	private void Deal() {
		Hand = BotTable.NewHand(BitConverter.ToInt32(RandomNumberGenerator.GetBytes(sizeof(int))));
		_radlcApplied = false;
		_handRecorded = false;
		_journaledEvents = 0;
		_turnSeat = null;
		_turnStartedAt = null;

		foreach (Chair chair in _chairs) {
			chair.Reserve = Options.ReserveSeconds;
		}

		Table($"Partija {_records.Count + 1}.");
		HeaderChanged();
	}

	private bool RecordIfFinished(DateTimeOffset now) {
		if (Hand is not { Phase: GamePhase.Finished } hand || _handRecorded) {
			return false;
		}

		_handRecorded = true;
		BotTable.Record(Sheet, hand);

		_records.Add(new HandRecord(
			Number: _records.Count + 1,
			Seed: hand.Seed,
			Contract: hand.PlayedContract!.Value,
			Declarer: hand.Declarer!.Value,
			DeclarerWon: hand.Score!.DeclarerWon,
			Scores: [.. hand.Score.BySeat],
			Events: [.. hand.Events]));

		Table($"{_chairs[hand.Declarer!.Value].Name}: {Contracts.Info(hand.PlayedContract!.Value).SlovenianName} — "
			+ (hand.Score.DeclarerWon ? "dobljeno" : "izgubljeno") + ".");

		if (_records.Count >= Options.Rounds) {
			Phase = TablePhase.Finished;
			Table("Konec mize. Hvala za igro.");
		} else {
			_dealNextAt = now + BetweenHands;
		}

		HeaderChanged();

		return true;
	}

	/// <summary>As soon as the auction names a declarer, the sheet says whether the hand counts double.</summary>
	private void ApplyRadlc() {
		if (_radlcApplied || Hand is not HandState hand || hand.Declarer is not int declarer || hand.Phase == GamePhase.Bidding) {
			return;
		}

		hand.ApplyRadlcMultiplier(Sheet.RadlcMultiplierFor(declarer));
		_radlcApplied = true;
	}

	public SessionStats Stats() {
		TableSnapshot snapshot = Snapshot;

		return SessionSummary.Build(snapshot.Records, Sheet, seat => snapshot.Seats[seat].Name);
	}

	private void Table(string text) => Add(new ChatLine(DateTimeOffset.UtcNow, -1, "miza", text, FromTable: true));

	private void Add(ChatLine line) {
		_chat.Add(line);

		if (_chat.Count > MaxChatLines) {
			_chat.RemoveRange(0, _chat.Count - MaxChatLines);
		}
	}

	private void Touch() => LastActivity = DateTimeOffset.UtcNow;

	private void Notify() => Changed?.Invoke();

	/// <summary>Build the snapshot and flush new events to the journal. Called inside the gate, on the way out.</summary>
	private void Publish() => Volatile.Write(ref _snapshot, Build());

	private TableSnapshot Build() {
		HandState? hand = Hand;
		PlayerView?[] views = new PlayerView?[TarokConstants.PlayerCount];
		PlayerView? spectator = null;

		if (hand is not null) {
			for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
				views[seat] = PlayerView.For(hand, seat);
			}

			spectator = PlayerView.ForSpectator(hand);

			if (_journal is not null && hand.Events.Count > _journaledEvents && !Faulted) {
				GameEvent[] fresh = [.. hand.Events.Skip(_journaledEvents)];
				int handNumber = _handRecorded ? _records.Count : _records.Count + 1;
				_journal.EventsAppended(Id, handNumber, _journaledEvents, fresh);
				_journaledEvents = hand.Events.Count;
			}
		}

		int number = _records.Count + (hand is null || hand.Phase == GamePhase.Finished ? 0 : 1);

		return new TableSnapshot(
			Phase,
			number,
			[.. _chairs.Select(chair => chair.Freeze())],
			[.. _chat],
			[.. _records],
			views,
			spectator,
			_watching,
			_turnSeat,
			_turnStartedAt,
			LastActivity);
	}

	private void HeaderChanged() => _journal?.TableChanged(this);

	// --- the journal's view of the table ---

	public sealed record SeatDto(int Index, string? Id, string? Name, int Rating, string? UserId, bool Abandoned, bool BotIsPlaying);

	public sealed record HeaderDto(TableOptions Options, string HostId, string HostName, int HostRating, string? HostUserId, IReadOnlyList<SeatDto> Seats, IReadOnlyList<ChatLine> Chat);

	/// <summary>Everything a restart needs besides the event log, as one JSON document.</summary>
	public LiveTableRecord ToRecord() {
		TableSnapshot snapshot = Snapshot;

		HeaderDto header = new(
			Options,
			Host.Id,
			Host.Name,
			Host.Rating,
			Host.UserId,
			[.. snapshot.Seats.Select(seat => new SeatDto(seat.Index, seat.Player?.Id, seat.Player?.Name, seat.Player?.Rating ?? 0, seat.Player?.UserId, seat.Abandoned, seat.BotIsPlaying))],
			snapshot.Chat);

		return new LiveTableRecord(
			Id,
			Options.Name,
			Host.Id,
			OptionsJson: JsonSerializer.Serialize(header, JsonOptions),
			SeatsJson: "",
			ChatJson: "",
			(int)snapshot.Phase,
			OpenedAt,
			StartedAt,
			DateTimeOffset.UtcNow,
			Archived);
	}

	/// <summary>
	/// Put a table back the way the journal left it: options, chairs, chat, every finished hand
	/// replayed onto the sheet, and the hand that was being played replayed up to its last event.
	/// Everybody is marked away, so a seat whose owner does not come back goes to a bot after the
	/// usual grace; clocks start from the full reserve.
	/// </summary>
	public static OnlineTable? Restore(LiveTableSnapshot saved, ITableJournal? journal, ChatFilter? filter, IReadOnlySet<string>? hostBlocks = null) {
		ArgumentNullException.ThrowIfNull(saved);

		HeaderDto? header;
		try {
			header = JsonSerializer.Deserialize<HeaderDto>(saved.Table.OptionsJson, JsonOptions);
		} catch (JsonException) {
			return null;
		}

		if (header is null) {
			return null;
		}

		PlayerIdentity host = new(header.HostId, header.HostName, header.HostRating) { UserId = header.HostUserId };
		OnlineTable table = new(saved.Table.Id, header.Options, host, journal, hostBlocks, filter);

		table._gate.Wait();
		try {
			table.OpenedAt = saved.Table.OpenedAt;
			table.StartedAt = saved.Table.StartedAt;
			table._chat.Clear();
			table._chat.AddRange(header.Chat.TakeLast(MaxChatLines));

			DateTimeOffset now = DateTimeOffset.UtcNow;

			foreach (SeatDto seat in header.Seats) {
				if (seat.Index < 0 || seat.Index >= TarokConstants.PlayerCount || seat.Id is null) {
					continue;
				}

				Chair chair = table._chairs[seat.Index];
				chair.Player = new PlayerIdentity(seat.Id, seat.Name ?? "?", seat.Rating) { UserId = seat.UserId };
				chair.Abandoned = seat.Abandoned;
				chair.BotIsPlaying = seat.BotIsPlaying;
				chair.Connections = 0;
				chair.AwaySince = now;
				chair.Reserve = table.Options.ReserveSeconds;
			}

			foreach (LiveHandRecord hand in saved.Hands.OrderBy(hand => hand.HandNumber)) {
				if (hand.Events.Count == 0) {
					continue;
				}

				HandState replayed;
				try {
					replayed = HandState.Replay(EventCodec.DecodeAll(hand.Events));
				} catch (Exception error) when (error is InvalidOperationException or ArgumentException or FormatException) {
					// A log this table cannot replay is a hand it cannot continue; the next deal starts fresh.
					table.Table($"Partije {hand.HandNumber} ni bilo mogoče obnoviti.");
					continue;
				}

				table.Hand = replayed;
				table._handRecorded = false;
				table._journaledEvents = replayed.Events.Count;
				table._radlcApplied = replayed.Events.Any(gameEvent => gameEvent is RadlcApplied);

				if (replayed.Phase == GamePhase.Finished) {
					table.RecordIfFinished(now);
					table._dealNextAt = null;
					table.Hand = null;
				}
			}

			table.Phase = (TablePhase)saved.Table.Phase;

			if (table.Phase == TablePhase.Playing) {
				if (table._records.Count >= table.Options.Rounds) {
					table.Phase = TablePhase.Finished;
				} else if (table.Hand is null) {
					table._dealNextAt = now + BetweenHands;
				}
			}

			table.Table("Miza je obnovljena po ponovnem zagonu strežnika.");
		} finally {
			table.Publish();
			table._gate.Release();
		}

		return table;
	}

	public void Dispose() {
		if (_disposed) {
			return;
		}

		_disposed = true;
		_gate.Dispose();
	}
}
