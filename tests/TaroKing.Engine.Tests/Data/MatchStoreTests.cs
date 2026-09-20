using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using TaroKing.App.Services;
using TaroKing.Data;
using TaroKing.Engine.Bidding;
using TaroKing.Engine.Session;

namespace TaroKing.Engine.Tests.Data;

/// <summary>
/// The store against a real SQLite database held in memory: a finished game shows up in history,
/// moves the rating, and the leaderboard agrees with the users' own numbers.
/// </summary>
public sealed class MatchStoreTests : IDisposable {

	private readonly InMemoryFactory _factory = new();
	private readonly MatchStore _store;

	public MatchStoreTests() {
		_store = new MatchStore(_factory);
	}

	public void Dispose() => _factory.Dispose();

	// --- the acceptance criteria ---

	[Fact]
	public async Task A_finished_online_match_shows_up_in_history_and_moves_the_rating() {
		await SeedUsersAsync("ana", "boris");

		FinishedMatch finished = await PlayedMatchAsync(MatchKind.Online, seed: 501, rounds: 4,
			seatOwners: ["ana", null, "boris", null]);

		int id = await _store.SaveAsync(finished);

		IReadOnlyList<MatchSummary> anaHistory = await _store.HistoryAsync("ana");
		IReadOnlyList<MatchSummary> borisHistory = await _store.HistoryAsync("boris");

		Assert.Single(anaHistory);
		Assert.Single(borisHistory);
		Assert.Equal(id, anaHistory[0].Id);
		Assert.True(anaHistory[0].Rated);
		Assert.NotNull(anaHistory[0].YourRatingDelta);

		TaroKingUser ana = (await _store.UserAsync("ana"))!;
		TaroKingUser boris = (await _store.UserAsync("boris"))!;

		// Somebody finished ahead of the other, so somebody moved — and in opposite directions.
		int anaDelta = ana.Rating - TaroKingUser.StartingRating;
		int borisDelta = boris.Rating - TaroKingUser.StartingRating;

		Assert.Equal(-anaDelta, borisDelta);
		Assert.Equal(1, ana.RatedMatches);
		Assert.Equal(1, ana.MatchesPlayed);

		int anaFinal = finished.Seats.First(seat => seat.UserId == "ana").FinalTotal;
		int borisFinal = finished.Seats.First(seat => seat.UserId == "boris").FinalTotal;

		if (anaFinal > borisFinal) {
			Assert.True(anaDelta > 0);
		} else if (anaFinal < borisFinal) {
			Assert.True(anaDelta < 0);
		} else {
			Assert.Equal(0, anaDelta);
		}
	}

	[Fact]
	public async Task The_leaderboard_matches_the_totals() {
		await SeedUsersAsync("ana", "boris", "cilka");

		await _store.SaveAsync(await PlayedMatchAsync(MatchKind.Online, 77, 4, ["ana", "boris", null, null]));
		await _store.SaveAsync(await PlayedMatchAsync(MatchKind.Online, 78, 4, ["cilka", null, "ana", null]));

		IReadOnlyList<LeaderboardEntry> board = await _store.LeaderboardAsync();

		Assert.Equal(3, board.Count);
		Assert.Equal(new[] { 1, 2, 3 }, board.Select(entry => entry.Place));

		for (int index = 1; index < board.Count; index++) {
			Assert.True(board[index - 1].Rating >= board[index].Rating);
		}

		foreach (LeaderboardEntry entry in board) {
			TaroKingUser user = (await _store.UserAsync(entry.UserId))!;
			Assert.Equal(user.Rating, entry.Rating);
			Assert.Equal(user.RatedMatches, entry.RatedMatches);
		}

		// Two rated matches, two rated players each: the book has four lines.
		await using TaroKingDbContext db = _factory.CreateDbContext();
		Assert.Equal(4, await db.Ratings.CountAsync());
	}

	[Fact]
	public async Task A_game_against_bots_is_remembered_but_never_rated() {
		await SeedUsersAsync("ana");

		await _store.SaveAsync(await PlayedMatchAsync(MatchKind.Local, 9, 3, ["ana", null, null, null]));

		IReadOnlyList<MatchSummary> history = await _store.HistoryAsync("ana");
		TaroKingUser ana = (await _store.UserAsync("ana"))!;

		Assert.Single(history);
		Assert.False(history[0].Rated);
		Assert.Null(history[0].YourRatingDelta);
		Assert.Equal(TaroKingUser.StartingRating, ana.Rating);
		Assert.Equal(0, ana.RatedMatches);
		Assert.Equal(1, ana.MatchesPlayed);
		Assert.Empty(await _store.LeaderboardAsync());
	}

	[Fact]
	public async Task Two_members_at_an_online_table_are_needed_before_it_counts() {
		await SeedUsersAsync("ana");

		await _store.SaveAsync(await PlayedMatchAsync(MatchKind.Online, 10, 3, ["ana", null, null, null]));

		Assert.False((await _store.HistoryAsync("ana"))[0].Rated);
	}

	// --- reading a match back ---

	[Fact]
	public async Task A_stored_hand_replays_from_the_database() {
		await SeedUsersAsync("ana", "boris");
		FinishedMatch finished = await PlayedMatchAsync(MatchKind.Online, 300, 3, ["ana", "boris", null, null]);

		int id = await _store.SaveAsync(finished);
		Match? match = await _store.MatchAsync(id);

		Assert.NotNull(match);
		Assert.Equal(3, match!.Hands.Count);
		Assert.Equal(4, match.Seats.Count);

		foreach (Hand hand in match.Hands) {
			IReadOnlyList<GameEvent> events = await _store.EventsAsync(hand.Id);
			HandState replayed = HandState.Replay(events);

			Assert.Equal(GamePhase.Finished, replayed.Phase);
			Assert.Equal(hand.Contract, replayed.PlayedContract);
			Assert.Equal(hand.ScoreFor(0), replayed.Score![0]);
			Assert.Equal(hand.ScoreFor(3), replayed.Score[3]);
		}
	}

	[Fact]
	public async Task History_is_newest_first_and_capped() {
		await SeedUsersAsync("ana");

		for (int index = 0; index < MatchStore.HistoryLength + 3; index++) {
			FinishedMatch finished = await PlayedMatchAsync(MatchKind.Local, 1000 + index, 1, ["ana", null, null, null]);
			await _store.SaveAsync(finished with { FinishedAt = DateTimeOffset.UtcNow.AddMinutes(index) });
		}

		IReadOnlyList<MatchSummary> history = await _store.HistoryAsync("ana");

		Assert.Equal(MatchStore.HistoryLength, history.Count);

		for (int index = 1; index < history.Count; index++) {
			Assert.True(history[index - 1].FinishedAt >= history[index].FinishedAt);
		}
	}

	// --- statistics ---

	[Fact]
	public async Task The_statistics_count_what_the_hands_say() {
		await SeedUsersAsync("ana");
		FinishedMatch finished = await PlayedMatchAsync(MatchKind.Local, 4444, 8, ["ana", null, null, null]);

		await _store.SaveAsync(finished);
		PlayerStats stats = await _store.StatsAsync("ana");

		int declared = finished.Hands.Count(hand => hand.Declarer == 0 && !Contracts.Info(hand.Contract).IsKlop);
		int announced = finished.Hands.Count(hand => hand.PagatUltimoAnnouncedBy == 0);

		Assert.Equal(1, stats.MatchesPlayed);
		Assert.Equal(8, stats.HandsPlayed);
		Assert.Equal(declared, stats.HandsDeclared);
		Assert.Equal(declared, stats.Contracts.Sum(record => record.Played));
		Assert.Equal(announced, stats.PagatUltimoAnnounced);
		Assert.True(stats.PagatUltimoConverted <= stats.PagatUltimoAnnounced);
		Assert.True(stats.HandsDeclaredWon <= stats.HandsDeclared);
	}

	[Fact]
	public async Task A_player_with_no_history_has_empty_statistics() {
		await SeedUsersAsync("ana");

		PlayerStats stats = await _store.StatsAsync("ana");

		Assert.Equal(0, stats.HandsPlayed);
		Assert.Empty(stats.Contracts);
		Assert.Equal(0, stats.PagatUltimoConversion);
	}

	// --- flags ---

	[Fact]
	public async Task A_report_is_counted_once_per_reporter() {
		await SeedUsersAsync("ana", "boris", "cilka");

		Assert.True(await _store.ReportAsync("ana", "boris", "vstal sredi igre"));
		Assert.False(await _store.ReportAsync("ana", "boris", "spet"));
		Assert.True(await _store.ReportAsync("cilka", "boris", ""));
		Assert.False(await _store.ReportAsync("boris", "boris", "sam sebe"));

		IReadOnlyDictionary<string, PlayerFlags> flags = await _store.FlagsAsync(["ana", "boris", "nobody"]);

		Assert.Equal(2, flags["boris"].Reports);
		Assert.Equal(0, flags["ana"].Reports);
		Assert.True(flags["ana"].IsMember);
		Assert.False(flags.ContainsKey("nobody"));
	}

	[Fact]
	public async Task Walking_out_often_enough_earns_the_leaver_flag() {
		await SeedUsersAsync("ana", "boris");

		for (int index = 0; index < 4; index++) {
			FinishedMatch finished = await PlayedMatchAsync(MatchKind.Online, 2000 + index, 1, ["ana", "boris", null, null]);

			// Ana walks out of half of them.
			bool left = index % 2 == 0;
			FinishedMatch marked = finished with {
				Seats = [.. finished.Seats.Select(seat => seat.UserId == "ana" ? seat with { Abandoned = left } : seat)]
			};

			await _store.SaveAsync(marked);
		}

		IReadOnlyDictionary<string, PlayerFlags> flags = await _store.FlagsAsync(["ana", "boris"]);

		Assert.True(flags["ana"].IsFrequentLeaver);
		Assert.False(flags["boris"].IsFrequentLeaver);
	}

	// --- helpers ---

	private async Task SeedUsersAsync(params string[] names) {
		await using TaroKingDbContext db = _factory.CreateDbContext();

		foreach (string name in names) {
			db.Users.Add(new TaroKingUser { Id = name, UserName = name, NormalizedUserName = name.ToUpperInvariant() });
		}

		await db.SaveChangesAsync();
	}

	/// <summary>A real session played by bots, dressed up as whichever kind of match the test wants.</summary>
	private static async Task<FinishedMatch> PlayedMatchAsync(MatchKind kind, int seed, int rounds, string?[] seatOwners) {
		using LocalGame game = new("played", new SessionOptions { HandCount = rounds, Seed = seed, AutoPlayHuman = true, BotDelayMs = 0 });
		await game.PlayOutAsync();

		IReadOnlyList<int> finals = game.Sheet.FinalTotals();

		List<FinishedSeat> seats = [];
		for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
			string? owner = seatOwners[seat];

			seats.Add(new FinishedSeat(
				Seat: seat,
				UserId: owner,
				Name: owner ?? $"Bot {seat + 1}",
				IsBot: owner is null,
				Abandoned: false,
				Total: game.Sheet.Totals[seat],
				FinalTotal: finals[seat],
				Radlci: game.Sheet.Radlci[seat]));
		}

		List<FinishedHand> hands = [.. game.Records.Select(record => FinishedHand.From(HandState.Replay(record.Events), record.Number))];

		return new FinishedMatch(
			Kind: kind,
			SourceId: $"seed-{seed}",
			Name: $"test {seed}",
			Rounds: rounds,
			StartedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
			FinishedAt: DateTimeOffset.UtcNow,
			Seats: seats,
			Hands: hands,
			Chat: [new FinishedChat(DateTimeOffset.UtcNow, -1, "miza", "test", true)]);
	}

	/// <summary>One open in-memory SQLite connection, shared by every context the store opens.</summary>
	private sealed class InMemoryFactory : IDbContextFactory<TaroKingDbContext>, IDisposable {

		private readonly SqliteConnection _connection = new("DataSource=:memory:");

		public InMemoryFactory() {
			_connection.Open();

			using TaroKingDbContext db = CreateDbContext();
			db.Database.EnsureCreated();
		}

		public TaroKingDbContext CreateDbContext() =>
			new(new DbContextOptionsBuilder<TaroKingDbContext>().UseSqlite(_connection).Options);

		public void Dispose() => _connection.Dispose();
	}
}
