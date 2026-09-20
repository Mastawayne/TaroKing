using System.Security.Cryptography;

using TaroKing.Bots;
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

/// <summary>One line of table talk. Seat -1 is the table itself.</summary>
public sealed record ChatLine(DateTimeOffset At, int Seat, string Who, string Text, bool FromTable = false);

/// <summary>
/// One chair. It knows who is sitting in it, whether they are currently connected, and how much
/// clock they have left.
/// </summary>
public sealed class TableSeat(int index) {

	public int Index { get; } = index;

	public PlayerIdentity? Player { get; set; }

	/// <summary>How many browser circuits this player currently has open on this table.</summary>
	public int Connections { get; set; }

	/// <summary>Since when the seat has been empty of a live connection. Null while somebody is here.</summary>
	public DateTimeOffset? AwaySince { get; set; }

	/// <summary>A bot is playing this seat for now — either nobody ever sat, or they walked off.</summary>
	public bool BotIsPlaying { get; set; }

	/// <summary>The owner stood up during play. The seat stays theirs; the mark stays on them.</summary>
	public bool Abandoned { get; set; }

	/// <summary>Seconds of thinking time in hand, beyond the per-move increment.</summary>
	public double Reserve { get; set; }

	public bool IsTaken => Player is not null;

	public string Name => Player?.Name ?? $"Bot {Index + 1}";
}

/// <summary>
/// One online table: four seats, one authoritative hand, and everything anybody is allowed to know
/// about it. Nothing here trusts a client. A move arrives with the identity that sent it, is
/// matched to the seat that identity actually owns, and then goes through the same engine calls a
/// bot's move would — so a forged seat number or an illegal card is refused, not obeyed.
///
/// Every player gets their own <see cref="PlayerView"/>. Because this is Blazor Server, the client
/// is only ever sent rendered HTML for its own view, so another player's cards never cross the wire
/// at all.
/// </summary>
public sealed class OnlineTable : IDisposable {

	/// <summary>How long a seat is held for somebody who drops before a bot takes over.</summary>
	public static readonly TimeSpan ReconnectGrace = TimeSpan.FromSeconds(60);

	/// <summary>How long a bot waits before answering, so the table does not blur past.</summary>
	public static readonly TimeSpan BotPace = TimeSpan.FromMilliseconds(900);

	/// <summary>The pause between a finished hand and the next deal.</summary>
	public static readonly TimeSpan BetweenHands = TimeSpan.FromSeconds(6);

	private const int MaxChatLines = 80;

	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly TableSeat[] _seats;
	private readonly IPlayerAgent[] _agents = new IPlayerAgent[TarokConstants.PlayerCount];
	private readonly List<HandRecord> _records = [];
	private readonly List<ChatLine> _chat = [];
	private readonly Dictionary<string, string> _notices = [];

	private DateTimeOffset? _turnStartedAt;
	private int? _turnSeat;
	private DateTimeOffset? _dealNextAt;
	private bool _radlcApplied;
	private bool _handRecorded;
	private bool _disposed;

	public OnlineTable(string id, TableOptions options, PlayerIdentity host) {
		ArgumentException.ThrowIfNullOrWhiteSpace(id);
		ArgumentNullException.ThrowIfNull(options);

		Id = id;
		Options = options.Clamped();
		Host = host;
		LastActivity = DateTimeOffset.UtcNow;

		_seats = [.. Enumerable.Range(0, TarokConstants.PlayerCount).Select(seat => new TableSeat(seat) {
			Reserve = Options.ReserveSeconds
		})];

		for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
			_agents[seat] = new HeuristicBot(BitConverter.ToInt32(RandomNumberGenerator.GetBytes(sizeof(int))), BotProfile.Normal);
		}

		Table($"Miza »{Options.Name}« je odprta: {Options.Describe()}.");
	}

	/// <summary>Raised whenever anything changes that a watching page would want to redraw.</summary>
	public event Action? Changed;

	public string Id { get; }

	public TableOptions Options { get; }

	public PlayerIdentity Host { get; }

	public DateTimeOffset LastActivity { get; private set; }

	/// <summary>When the first hand was dealt. Null while the table is still waiting.</summary>
	public DateTimeOffset? StartedAt { get; private set; }

	/// <summary>Set by whoever writes the finished table down, so it is written once.</summary>
	public bool Archived { get; set; }

	public TablePhase Phase { get; private set; } = TablePhase.Waiting;

	public HandState? Hand { get; private set; }

	public ScoreSheet Sheet { get; } = new();

	public IReadOnlyList<TableSeat> Seats => _seats;

	public IReadOnlyList<HandRecord> Records => _records;

	public IReadOnlyList<ChatLine> Chat => _chat;

	public int HandsPlayed => _records.Count;

	public int HandNumber => _records.Count + (Hand is null || Hand.Phase == GamePhase.Finished ? 0 : 1);

	public int HumansSeated => _seats.Count(seat => seat.IsTaken);

	public int Watching { get; private set; }

	public bool IsFull => _seats.All(seat => seat.IsTaken);

	public string NameOf(int seat) =>
		seat >= 0 && seat < _seats.Length ? _seats[seat].Name : "miza";

	// --- sitting down and standing up ---

	/// <summary>Take a seat. Returns null on success, or the reason it was refused.</summary>
	public string? Sit(PlayerIdentity player, int seat) {
		if (!player.IsKnown) {
			return "Najprej se predstavi.";
		}

		if (seat < 0 || seat >= _seats.Length) {
			return "Takega mesta ni.";
		}

		if (Phase == TablePhase.Finished) {
			return "Miza je odigrana.";
		}

		if (Options.MembersOnly && !player.IsMember) {
			return "Ta miza je samo za prijavljene.";
		}

		if (Options.MinimumRating > 0 && player.Rating < Options.MinimumRating) {
			return $"Za to mizo je treba imeti vsaj {Options.MinimumRating} točk.";
		}

		if (SeatOf(player) is int already) {
			return already == seat ? null : "Že sediš za to mizo.";
		}

		TableSeat chair = _seats[seat];
		if (chair.IsTaken) {
			return "Mesto je zasedeno.";
		}

		chair.Player = player;

		// They are looking at the table right now, so they are present, not away.
		chair.Connections = 1;
		chair.AwaySince = null;
		chair.BotIsPlaying = false;
		chair.Reserve = Options.ReserveSeconds;
		Watching = Math.Max(0, Watching - 1);

		Table($"{player.Name} sede na mesto {seat + 1}.");
		Touch();
		Notify();

		return null;
	}

	/// <summary>
	/// Leave the table. Before the first deal the seat is simply freed. During play the seat stays
	/// theirs — a bot plays it, they may come back to it — and the walk-out is held against them.
	/// </summary>
	public void Stand(PlayerIdentity player) {
		if (SeatOf(player) is not int seat) {
			return;
		}

		TableSeat chair = _seats[seat];

		if (Phase == TablePhase.Playing) {
			chair.BotIsPlaying = true;
			chair.Abandoned = true;
			chair.Connections = 0;
			chair.AwaySince = DateTimeOffset.UtcNow;
			Watching++;

			Table($"{player.Name} je vstal sredi igre — mesto igra bot.");
		} else {
			chair.Player = null;
			chair.Connections = 0;
			chair.AwaySince = null;
			chair.BotIsPlaying = false;
			chair.Abandoned = false;
			Watching++;

			Table($"{player.Name} je vstal od mize.");
		}

		Touch();
		Notify();
	}

	/// <summary>Sit back down in your own chair after standing up mid-game. The walk-out still counts.</summary>
	public void Rejoin(PlayerIdentity player) {
		if (SeatOf(player) is not int seat || !_seats[seat].BotIsPlaying) {
			return;
		}

		TableSeat chair = _seats[seat];
		chair.BotIsPlaying = false;
		chair.AwaySince = null;
		chair.Connections = Math.Max(1, chair.Connections);
		Watching = Math.Max(0, Watching - 1);

		Table($"{chair.Name} je spet za mizo.");
		Touch();
		Notify();
	}

	/// <summary>A page opened on this table.</summary>
	public void Attach(PlayerIdentity player) {
		if (SeatOf(player) is int seat) {
			TableSeat chair = _seats[seat];
			chair.Connections++;
			chair.AwaySince = null;

			if (chair.BotIsPlaying) {
				chair.BotIsPlaying = false;
				Watching = Math.Max(0, Watching - 1);
				Table($"{chair.Name} je spet za mizo.");
			}
		} else {
			Watching++;
		}

		Touch();
		Notify();
	}

	/// <summary>A page closed. The seat is held for <see cref="ReconnectGrace"/> before a bot takes it.</summary>
	public void Detach(PlayerIdentity player) {
		if (SeatOf(player) is int seat) {
			TableSeat chair = _seats[seat];
			chair.Connections = Math.Max(0, chair.Connections - 1);

			if (chair.Connections == 0) {
				chair.AwaySince = DateTimeOffset.UtcNow;
			}
		} else {
			Watching = Math.Max(0, Watching - 1);
		}

		Notify();
	}

	public int? SeatOf(PlayerIdentity player) {
		if (!player.IsKnown) {
			return null;
		}

		foreach (TableSeat seat in _seats) {
			if (seat.Player?.Id == player.Id) {
				return seat.Index;
			}
		}

		return null;
	}

	/// <summary>What this person is allowed to see. A watcher gets the table, never a hand.</summary>
	public PlayerView? ViewFor(PlayerIdentity player) {
		if (Hand is not HandState hand) {
			return null;
		}

		return SeatOf(player) is int seat ? PlayerView.For(hand, seat) : PlayerView.ForSpectator(hand);
	}

	/// <summary>Seconds this seat has before the clock runs out, as of now.</summary>
	public double ClockFor(int seat, DateTimeOffset now) {
		if (seat < 0 || seat >= _seats.Length) {
			return 0;
		}

		double budget = Options.SecondsPerMove + _seats[seat].Reserve;

		return _turnSeat == seat && _turnStartedAt is DateTimeOffset since
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

	public void Say(PlayerIdentity player, string text) {
		if (string.IsNullOrWhiteSpace(text)) {
			return;
		}

		string trimmed = text.Trim();
		trimmed = trimmed[..Math.Min(trimmed.Length, 200)];

		Add(new ChatLine(DateTimeOffset.UtcNow, SeatOf(player) ?? -1, player.Name, trimmed));
		Touch();
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

				bool botsTurn = IsBotPlaying(seat);
				bool outOfTime = !botsTurn && ClockFor(seat, now) <= 0;

				if (outOfTime) {
					Table($"{NameOf(seat)} je porabil čas — potezo odigra bot.");
					_seats[seat].Reserve = 0;
				}

				if ((botsTurn && now - _turnStartedAt >= BotPace) || outOfTime) {
					await BotTable.ActAsync(hand, _agents[seat], seat);
					AfterMove(seat, now);
					changed = true;
				}
			}

			changed |= RecordIfFinished(now);
		} catch (Exception error) when (error is InvalidOperationException or ArgumentException) {
			Table($"Napaka za mizo: {error.Message}");
			changed = true;
		} finally {
			_gate.Release();
		}

		if (changed) {
			Notify();
		}
	}

	// --- the machinery ---

	private async Task ActAsync(PlayerIdentity player, Action<HandState, int> action) {
		if (!await _gate.WaitAsync(TimeSpan.FromSeconds(5))) {
			return;
		}

		try {
			Touch();

			if (player.IsKnown) {
				_notices.Remove(player.Id);
			}

			if (SeatOf(player) is not int seat) {
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
		if (_turnSeat == seat && _turnStartedAt is DateTimeOffset since && !IsBotPlaying(seat)) {
			double spent = (now - since).TotalSeconds;
			double reserve = _seats[seat].Reserve + Options.SecondsPerMove - spent;

			_seats[seat].Reserve = Math.Clamp(reserve, 0, Options.ReserveSeconds);
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

	/// <summary>A seat nobody is connected to goes to a bot once the grace period is up.</summary>
	private bool HandSeatsToBots(DateTimeOffset now) {
		bool changed = false;

		foreach (TableSeat seat in _seats) {
			bool abandoned = seat.IsTaken
				&& seat.Connections == 0
				&& seat.AwaySince is DateTimeOffset since
				&& now - since > ReconnectGrace;

			if (abandoned && !seat.BotIsPlaying) {
				seat.BotIsPlaying = true;
				Table($"{seat.Name} se ni vrnil — mesto igra bot.");
				changed = true;
			}
		}

		return changed;
	}

	/// <summary>A seat is played by a bot when it is empty and filled, or its owner walked off.</summary>
	public bool IsBotPlaying(int seat) =>
		_seats[seat].BotIsPlaying || (!_seats[seat].IsTaken && Options.FillWithBots);

	private bool StartIfReady(DateTimeOffset now) {
		if (Phase != TablePhase.Waiting) {
			return false;
		}

		bool everySeatAccountedFor = Options.FillWithBots || IsFull;
		if (!everySeatAccountedFor || HumansSeated == 0) {
			return false;
		}

		Phase = TablePhase.Playing;
		StartedAt = now;
		Table("Začenjamo.");
		Deal();

		return true;
	}

	private bool DealIfDue(DateTimeOffset now) {
		if (Phase != TablePhase.Playing || _dealNextAt is not DateTimeOffset due || now < due) {
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
		_turnSeat = null;
		_turnStartedAt = null;

		foreach (TableSeat seat in _seats) {
			seat.Reserve = Options.ReserveSeconds;
		}

		Table($"Partija {HandNumber}.");
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

		Table($"{NameOf(hand.Declarer!.Value)}: {Contracts.Info(hand.PlayedContract!.Value).SlovenianName} — "
			+ (hand.Score.DeclarerWon ? "dobljeno" : "izgubljeno") + ".");

		if (_records.Count >= Options.Rounds) {
			Phase = TablePhase.Finished;
			Table("Konec mize. Hvala za igro.");
		} else {
			_dealNextAt = now + BetweenHands;
		}

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

	public SessionStats Stats() => SessionSummary.Build(_records, Sheet, NameOf);

	private void Table(string text) => Add(new ChatLine(DateTimeOffset.UtcNow, -1, "miza", text, FromTable: true));

	private void Add(ChatLine line) {
		_chat.Add(line);

		if (_chat.Count > MaxChatLines) {
			_chat.RemoveRange(0, _chat.Count - MaxChatLines);
		}
	}

	private void Touch() => LastActivity = DateTimeOffset.UtcNow;

	private void Notify() => Changed?.Invoke();

	public void Dispose() {
		if (_disposed) {
			return;
		}

		_disposed = true;
		_gate.Dispose();
	}
}
