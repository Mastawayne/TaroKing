using TaroKing.Bots;
using TaroKing.Engine;
using TaroKing.Engine.Announcing;
using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;
using TaroKing.Engine.Scoring;
using TaroKing.Engine.Session;

namespace TaroKing.App.Services;

/// <summary>
/// One human with three bots, for a whole session of hands.
///
/// It lives in <see cref="LocalGames"/> rather than in the circuit, so refreshing the browser finds
/// the same game where it was left — including a hand that is halfway through a trick. The page
/// never touches <see cref="HandState"/> directly: every human action comes through here, goes into
/// the engine exactly as a bot's would, and anything the engine refuses turns into a
/// <see cref="Notice"/> instead of an unhandled exception. That is the whole point — the client may
/// offer an illegal card, and the server still says no.
/// </summary>
public sealed class LocalGame : ITableActions, IDisposable {

	private static readonly string[] BotNames = ["Dušan", "Ana", "Boris", "Cilka"];

	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly IPlayerAgent[] _agents = new IPlayerAgent[TarokConstants.PlayerCount];
	private readonly List<HandRecord> _records = [];
	private readonly CancellationTokenSource _closing = new();

	private int _nextSeed;
	private bool _replayCompulsoryKlop;
	private bool _radlcApplied;
	private bool _handRecorded;
	private bool _disposed;

	public LocalGame(string id, SessionOptions options, PlayerIdentity? owner = null) {
		ArgumentException.ThrowIfNullOrWhiteSpace(id);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentOutOfRangeException.ThrowIfNegative(options.HumanSeat);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(options.HumanSeat, TarokConstants.PlayerCount);

		Id = id;
		Options = options;
		Owner = owner ?? PlayerIdentity.Unknown;
		Seat = options.HumanSeat;
		StartedAt = DateTimeOffset.UtcNow;
		_nextSeed = options.Seed ?? Random.Shared.Next();
		_replayCompulsoryKlop = options.ReplayCompulsoryKlop;
		BotDelay = TimeSpan.FromMilliseconds(options.BotDelayMs);
		Touch();
		SeatBots();
	}

	/// <summary>Raised after anything at all changes, so every open page can redraw.</summary>
	public event Action? Changed;

	/// <summary>Raised once, when the last hand of the session has been written on the sheet.</summary>
	public event Action<LocalGame>? Finished;

	public string Id { get; }

	public SessionOptions Options { get; }

	/// <summary>Who is playing. A guest's game is played and forgotten; a member's goes into their history.</summary>
	public PlayerIdentity Owner { get; }

	/// <summary>The chair the human is in.</summary>
	public int Seat { get; }

	public DateTimeOffset StartedAt { get; }

	/// <summary>When somebody last looked at this game, so an abandoned one can be swept up.</summary>
	public DateTimeOffset LastTouched { get; private set; }

	public ScoreSheet Sheet { get; } = new();

	public HandState? Hand { get; private set; }

	public IReadOnlyList<HandRecord> Records => _records;

	/// <summary>True once the session has played out whatever it was set to play.</summary>
	public bool IsOver { get; private set; }

	public int HandsPlayed => _records.Count;

	/// <summary>The hand now on the table, counting from one. Zero before the first deal.</summary>
	public int HandNumber => _records.Count + (Hand is null || HandIsOver ? 0 : 1);

	/// <summary>What the human is allowed to see and do. Null before the first deal.</summary>
	public PlayerView? View => Hand is null ? null : PlayerView.For(Hand, Seat);

	/// <summary>The last thing the engine refused. Cleared on the next action.</summary>
	public string? Notice { get; private set; }

	/// <summary>True while a bot is taking its turn, so the table stops taking clicks.</summary>
	public bool BotsAreThinking { get; private set; }

	public bool HandIsOver => Hand?.Phase == GamePhase.Finished;

	public bool ItIsYourTurn => Hand is { Phase: not GamePhase.Finished } && Hand.CurrentSeat == Seat;

	/// <summary>How long a bot pretends to think before answering.</summary>
	public TimeSpan BotDelay { get; set; }

	public string NameOf(int seat) {
		if (seat == Seat) {
			return "Ti";
		}

		return seat >= 0 && seat < BotNames.Length ? BotNames[seat] : $"Mesto {seat}";
	}

	public string SeatName(int seat) => NameOf(seat);

	/// <summary>Against bots the player decides when the next hand starts.</summary>
	public bool CanDealNext => HandIsOver && !IsOver;

	public Task DealNextAsync() => DealAsync();

	// --- the session ---

	/// <summary>Deal the next hand, unless the session has already played itself out.</summary>
	public Task DealAsync() => RunAsync(() => {
		if (IsOver) {
			throw new InvalidOperationException("Sezija je končana.");
		}

		// A replayed compulsory klop must be dealt exactly as it was, not re-judged from the cards.
		Hand = _replayCompulsoryKlop
			? HandState.Create(_nextSeed, compulsoryKlop: true)
			: BotTable.NewHand(_nextSeed);
		_replayCompulsoryKlop = false;

		// A deal that had to be thrown out took the next seed with it, so carry on from the one used.
		_nextSeed = unchecked(Hand.Seed + 1);
		_radlcApplied = false;
		_handRecorded = false;
	});

	/// <summary>
	/// Pick a hand back up after a page refresh: if the table is waiting on a bot and no loop is
	/// running, start one. Does nothing at all when a loop is already going.
	/// </summary>
	public Task ResumeAsync() => RunAsync(() => { }, TimeSpan.Zero);

	/// <summary>
	/// Play the whole session with nobody at the keyboard. Only sensible with
	/// <see cref="SessionOptions.AutoPlayHuman"/>, which is how the tests drive a full session.
	/// </summary>
	public async Task PlayOutAsync(CancellationToken cancellationToken = default) {
		// A session cannot need more turns than this; the guard stops a stuck hand spinning forever.
		int guard = (Options.Mode == SessionMode.Hands ? Options.HandCount : 200) + 10;

		while (!IsOver && guard-- > 0) {
			cancellationToken.ThrowIfCancellationRequested();

			if (Hand is null || HandIsOver) {
				await DealAsync();
			} else {
				await ResumeAsync();
			}

			if (Notice is string notice) {
				throw new InvalidOperationException(notice);
			}
		}

		if (!IsOver) {
			throw new InvalidOperationException("Sezija se ni iztekla — nekje se je zataknilo.");
		}
	}

	// --- what the human may do ---

	public Task BidAsync(Contract contract) => ActAsync(hand => hand.PlaceBid(Seat, contract));

	public Task PassBidAsync() => ActAsync(hand => hand.PassBid(Seat));

	public Task CallKingAsync(Suit suit) => ActAsync(hand => hand.CallKing(Seat, suit));

	public Task TakeTalonPacketAsync(int packet) => ActAsync(hand => hand.TakeTalonPacket(Seat, packet));

	public Task DiscardAsync(IReadOnlyList<Card> cards) => ActAsync(hand => hand.Discard(Seat, cards));

	public Task UpgradeAsync(bool upgrade) => ActAsync(hand => {
		if (upgrade) {
			hand.UpgradeToColourValat(Seat);
		} else {
			hand.KeepContract(Seat);
		}
	});

	public Task AnnounceAsync(Bonus bonus) => ActAsync(hand => hand.Announce(Seat, bonus));

	public Task KontraAsync(KontraTarget target) => ActAsync(hand => hand.Kontra(Seat, target));

	public Task PassAnnouncementAsync() => ActAsync(hand => hand.PassAnnouncement(Seat));

	public Task PlayCardAsync(Card card) => ActAsync(hand => hand.PlayCard(Seat, card));

	// --- the summary ---

	public SessionStats Stats() => SessionSummary.Build(_records, Sheet, NameOf);

	// --- the machinery ---

	/// <summary>
	/// Apply one human action and then let the bots run until it is the human's turn again.
	/// A refusal from the engine leaves the hand untouched and shows up as a notice.
	/// </summary>
	private Task ActAsync(Action<HandState> action) => RunAsync(() => {
		if (Hand is null) {
			throw new InvalidOperationException("Ni razdeljene partije.");
		}

		action(Hand);
	});

	private async Task RunAsync(Action work, TimeSpan? timeout = null) {
		if (!await _gate.WaitAsync(timeout ?? TimeSpan.FromSeconds(10), _closing.Token)) {
			return;
		}

		try {
			Touch();
			Notice = null;

			try {
				work();
			} catch (Exception error) when (error is InvalidOperationException or ArgumentException) {
				Notice = error.Message;
				return;
			}

			try {
				await DriveBotsAsync();
			} catch (Exception error) when (error is InvalidOperationException or ArgumentException) {
				// A bot that talks nonsense stops the hand rather than the circuit.
				Notice = $"Bot se je zmotil: {error.Message}";
			}
		} catch (OperationCanceledException) {
			// The game is being swept up; nothing to report.
		} finally {
			BotsAreThinking = false;
			_gate.Release();
			Notify();
		}
	}

	private async Task DriveBotsAsync() {
		while (Hand is { Phase: not GamePhase.Finished } hand && hand.CurrentSeat is int seat) {
			ApplyRadlc(hand);

			if (seat == Seat && !Options.AutoPlayHuman) {
				BotsAreThinking = false;
				Notify();
				return;
			}

			BotsAreThinking = true;
			Notify();

			if (BotDelay > TimeSpan.Zero) {
				await Task.Delay(BotDelay, _closing.Token);
			}

			await BotTable.ActAsync(hand, _agents[seat], seat, _closing.Token);
			Notify();
		}

		BotsAreThinking = false;

		if (Hand is { Phase: GamePhase.Finished } finished) {
			RecordHand(finished);
		}
	}

	/// <summary>Write the finished hand onto the sheet, once, and see whether the session is done.</summary>
	private void RecordHand(HandState hand) {
		if (_handRecorded) {
			return;
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

		IsOver = Options.Mode switch {
			SessionMode.Hands => _records.Count >= Options.HandCount,
			SessionMode.Target => Sheet.Totals.Any(total => total >= Options.TargetScore),
			_ => false
		};

		if (IsOver) {
			Finished?.Invoke(this);
		}
	}

	/// <summary>As soon as the auction names a declarer, the sheet says whether the hand counts double.</summary>
	private void ApplyRadlc(HandState hand) {
		if (_radlcApplied || hand.Declarer is not int declarer || hand.Phase == GamePhase.Bidding) {
			return;
		}

		hand.ApplyRadlcMultiplier(Sheet.RadlcMultiplierFor(declarer));
		_radlcApplied = true;
	}

	private void SeatBots() {
		int seed = _nextSeed;

		for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
			_agents[seat] = new HeuristicBot(
				unchecked((seed * TarokConstants.PlayerCount) + seat),
				Options.ProfileFor(seat),
				NameOf(seat));
		}
	}

	private void Touch() => LastTouched = DateTimeOffset.UtcNow;

	private void Notify() => Changed?.Invoke();

	public void Dispose() {
		if (_disposed) {
			return;
		}

		_disposed = true;
		_closing.Cancel();
		_closing.Dispose();
		_gate.Dispose();
	}
}
