using TaroKing.App.Services;
using TaroKing.Engine.Cards;
using TaroKing.Engine.Session;

namespace TaroKing.Engine.Tests.Session;

/// <summary>
/// The online table drives itself on a heartbeat rather than on a browser, so these tests just
/// hand it a clock and watch. Every test here is really one question: does the server stay the
/// authority when a client is absent, slow, lying, or only watching?
/// </summary>
public class OnlineTableTests {

	private static readonly PlayerIdentity Alice = new("alice", "Ana");
	private static readonly PlayerIdentity Bob = new("bob", "Boris");
	private static readonly PlayerIdentity Watcher = new("watcher", "Radovednež");

	/// <summary>
	/// The simulated clock starts from the real one, because a seat's away-time is stamped with
	/// the real clock when a connection drops — a fixed date in the past would never expire.
	/// </summary>
	private static readonly DateTimeOffset Start = DateTimeOffset.UtcNow;

	private static readonly TimeSpan AfterGrace = OnlineTable.ReconnectGrace + TimeSpan.FromSeconds(10);

	// --- a table plays itself ---

	[Fact]
	public async Task A_table_whose_seats_are_all_bots_plays_its_rounds_out() {
		using OnlineTable table = Abandoned(new TableOptions { Rounds = 7 });

		await BeatAsync(table, seconds: 1200);

		Assert.Equal(TablePhase.Finished, table.Phase);
		Assert.Equal(7, table.Records.Count);
		Assert.Equal(7, table.Sheet.Hands.Count);
	}

	[Fact]
	public async Task Every_hand_a_table_plays_replays_from_its_log() {
		using OnlineTable table = Abandoned(new TableOptions { Rounds = 7 });

		await BeatAsync(table, seconds: 1200);

		foreach (HandRecord record in table.Records) {
			HandState replayed = HandState.Replay(record.Events);

			Assert.Equal(GamePhase.Finished, replayed.Phase);
			Assert.Equal(record.Scores, replayed.Score!.BySeat);
		}
	}

	[Fact]
	public async Task Two_tables_never_deal_the_same_hand() {
		using OnlineTable first = Abandoned(new TableOptions { Rounds = 7 });
		using OnlineTable second = Abandoned(new TableOptions { Rounds = 7 });

		await BeatAsync(first, seconds: 1200);
		await BeatAsync(second, seconds: 1200);

		// The seed is cryptographic, so a collision across fourteen hands would be remarkable.
		Assert.Empty(first.Records.Select(record => record.Seed)
			.Intersect(second.Records.Select(record => record.Seed)));
	}

	// --- what a watcher may see ---

	[Fact]
	public async Task A_spectator_is_dealt_nothing_and_may_do_nothing() {
		using OnlineTable table = Abandoned(new TableOptions());

		await BeatAsync(table, seconds: 40);

		PlayerView view = table.ViewFor(Watcher)!;

		Assert.True(view.IsSpectator);
		Assert.Empty(view.Hand);
		Assert.Empty(view.LegalPlays);
		Assert.Empty(view.LegalBids);
		Assert.Empty(view.LegalDiscards);
		Assert.Empty(view.LegalAnnouncements);
		Assert.False(view.IsYourTurn);
	}

	[Fact]
	public async Task A_spectator_is_not_told_the_partner_before_the_table_is() {
		using OnlineTable table = Abandoned(new TableOptions());

		for (int second = 0; second < 400; second++) {
			await table.TickAsync(Start + AfterGrace + TimeSpan.FromSeconds(second));

			PlayerView view = table.ViewFor(Watcher)!;

			// Before a single card is down, nobody watching can know who is with whom.
			if (view.CompletedTricks.Count == 0 && view.CurrentTrick.Count == 0) {
				Assert.Null(view.KnownPartner);
			}
		}
	}

	[Fact]
	public async Task A_watcher_who_tries_to_play_is_told_they_are_only_watching() {
		using OnlineTable table = Abandoned(new TableOptions());

		await BeatAsync(table, seconds: 40);
		await table.PlayCardAsync(Watcher, Card.Skis);

		Assert.NotNull(table.NoticeFor(Watcher));
	}

	// --- seats ---

	[Fact]
	public void A_taken_seat_cannot_be_taken_again() {
		using OnlineTable table = new("t", new TableOptions(), Alice);

		Assert.Null(table.Sit(Alice, 0));
		Assert.NotNull(table.Sit(Bob, 0));
		Assert.Equal(0, table.SeatOf(Alice));
		Assert.Null(table.SeatOf(Bob));
	}

	[Fact]
	public void Sitting_down_twice_keeps_the_first_seat() {
		using OnlineTable table = new("t", new TableOptions(), Alice);

		Assert.Null(table.Sit(Alice, 0));
		Assert.NotNull(table.Sit(Alice, 2));
		Assert.Equal(0, table.SeatOf(Alice));
	}

	[Fact]
	public void A_table_with_a_minimum_rating_turns_away_anybody_below_it() {
		using OnlineTable table = new("t", new TableOptions { MinimumRating = 1500 }, Alice);

		Assert.NotNull(table.Sit(Alice, 0));
		Assert.Null(table.Sit(Alice with { Rating = 1600 }, 0));
	}

	// --- dropping out and coming back ---

	[Fact]
	public async Task A_seat_that_goes_quiet_is_played_by_a_bot_and_handed_back_on_return() {
		using OnlineTable table = new("t", new TableOptions(), Alice);

		table.Attach(Alice);
		Assert.Null(table.Sit(Alice, 0));
		Assert.False(table.IsBotPlaying(0));

		table.Detach(Alice);
		await table.TickAsync(DateTimeOffset.UtcNow + OnlineTable.ReconnectGrace + TimeSpan.FromSeconds(10));

		Assert.True(table.IsBotPlaying(0));

		table.Attach(Alice);

		Assert.False(table.IsBotPlaying(0));
		Assert.Equal(0, table.SeatOf(Alice));
	}

	[Fact]
	public async Task A_seat_is_held_for_the_whole_grace_period() {
		using OnlineTable table = new("t", new TableOptions(), Alice);

		table.Attach(Alice);
		table.Sit(Alice, 0);
		table.Detach(Alice);

		await table.TickAsync(DateTimeOffset.UtcNow + OnlineTable.ReconnectGrace - TimeSpan.FromSeconds(5));

		Assert.False(table.IsBotPlaying(0));
	}

	// --- the clock ---

	[Fact]
	public async Task The_table_waits_for_a_player_and_then_takes_the_move_away() {
		TableOptions options = new() { SecondsPerMove = 1.5, ReserveSeconds = 15 };
		using OnlineTable table = new("t", options, Alice);

		table.Attach(Alice);
		table.Sit(Alice, 0);

		// Beat until the hand is genuinely waiting on Ana.
		int second = 0;
		while (table.Hand?.CurrentSeat != 0 && second < 300) {
			await table.TickAsync(Start + TimeSpan.FromSeconds(second++));
		}

		Assert.Equal(0, table.Hand!.CurrentSeat);

		int events = table.Hand.Events.Count;

		// Inside her budget the table simply waits: a human seat is nobody else's to play.
		await table.TickAsync(Start + TimeSpan.FromSeconds(second) + TimeSpan.FromSeconds(1));
		Assert.Equal(events, table.Hand.Events.Count);

		// Past it, the move is made for her.
		await table.TickAsync(Start + TimeSpan.FromSeconds(second) + TimeSpan.FromSeconds(60));
		Assert.True(table.Hand.Events.Count > events, "The clock ran out and nothing happened.");
	}

	[Fact]
	public async Task A_move_out_of_turn_is_refused() {
		using OnlineTable table = new("t", new TableOptions(), Alice);

		table.Attach(Alice);
		table.Sit(Alice, 0);

		int second = 0;
		while (table.Hand is null && second < 50) {
			await table.TickAsync(Start + TimeSpan.FromSeconds(second++));
		}

		Assert.NotNull(table.Hand);

		// Forehand never opens the auction, so seat 0 cannot be on turn at the very first beat.
		if (table.Hand!.CurrentSeat != 0) {
			await table.PlayCardAsync(Alice, Card.Skis);
			Assert.NotNull(table.NoticeFor(Alice));
		}
	}

	// --- the lobby ---

	[Fact]
	public void A_private_table_stays_out_of_the_lobby() {
		using TableService tables = new();

		OnlineTable open = tables.Open(new TableOptions { Name = "Javna" }, Alice);
		OnlineTable hidden = tables.Open(new TableOptions { Name = "Skrita", Visibility = TableVisibility.Private }, Alice);

		IReadOnlyList<TableSummary> lobby = tables.Lobby();

		Assert.Contains(lobby, entry => entry.Id == open.Id);
		Assert.DoesNotContain(lobby, entry => entry.Id == hidden.Id);
		Assert.NotNull(tables.Find(hidden.Id));
	}

	[Fact]
	public void Table_options_are_pulled_back_into_their_range() {
		TableOptions clamped = new TableOptions { Rounds = 99, SecondsPerMove = 12, Name = "   " }.Clamped();

		Assert.Equal(TableOptions.MaxRounds, clamped.Rounds);
		Assert.Equal(TableOptions.MaxSecondsPerMove, clamped.SecondsPerMove);
		Assert.False(string.IsNullOrWhiteSpace(clamped.Name));
	}

	// --- helpers ---

	/// <summary>
	/// A table with one seat nominally taken but nobody connected, so the grace period expires and
	/// all four seats end up on bots. That is a whole table that plays without a browser.
	/// </summary>
	private static OnlineTable Abandoned(TableOptions options) {
		OnlineTable table = new("t", options, Alice);

		table.Attach(Alice);
		table.Sit(Alice, 0);
		table.Detach(Alice);

		return table;
	}

	/// <summary>One beat per simulated second, starting after the reconnect grace has passed.</summary>
	private static async Task BeatAsync(OnlineTable table, int seconds) {
		for (int second = 0; second < seconds; second++) {
			await table.TickAsync(Start + AfterGrace + TimeSpan.FromSeconds(second));
		}
	}
}
