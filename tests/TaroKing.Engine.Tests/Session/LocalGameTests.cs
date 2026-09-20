using TaroKing.App.Services;
using TaroKing.Engine.Cards;
using TaroKing.Engine.Session;

namespace TaroKing.Engine.Tests.Session;

/// <summary>
/// The session as the table UI drives it: deal, play, record, and stop when it is supposed to.
/// For the whole-session tests the human seat is handed to a bot, so a session can run without
/// a browser; the last two tests put the human back and check that the table waits for them.
/// </summary>
public class LocalGameTests {

	// --- a session plays itself out ---

	[Fact]
	public async Task Ten_hands_run_one_after_another_without_a_hitch() {
		using LocalGame game = Watching(new SessionOptions { HandCount = 10, Seed = 4242 });

		await game.PlayOutAsync();

		Assert.True(game.IsOver);
		Assert.Equal(10, game.HandsPlayed);
		Assert.Null(game.Notice);
		Assert.Equal(10, game.Sheet.Hands.Count);
	}

	[Fact]
	public async Task The_sheet_and_the_records_tell_the_same_story() {
		using LocalGame game = Watching(new SessionOptions { HandCount = 6, Seed = 77 });

		await game.PlayOutAsync();

		for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
			int current = seat;
			Assert.Equal(game.Records.Sum(record => record.Scores[current]), game.Sheet.Totals[seat]);
		}
	}

	[Fact]
	public async Task A_session_played_to_a_target_stops_when_somebody_gets_there() {
		using LocalGame game = Watching(new SessionOptions {
			Mode = SessionMode.Target,
			TargetScore = 120,
			Seed = 9001
		});

		await game.PlayOutAsync();

		Assert.True(game.IsOver);
		Assert.True(
			game.Sheet.Totals.Max() >= 120,
			$"Nobody reached the target: {string.Join(", ", game.Sheet.Totals)}.");
	}

	[Fact]
	public async Task A_finished_session_refuses_another_deal() {
		using LocalGame game = Watching(new SessionOptions { HandCount = 1, Seed = 5 });

		await game.PlayOutAsync();
		await game.DealAsync();

		Assert.Equal(1, game.HandsPlayed);
		Assert.NotNull(game.Notice);
	}

	// --- the same seed is the same session ---

	[Fact]
	public async Task The_same_seed_deals_the_same_session_twice() {
		using LocalGame first = Watching(new SessionOptions { HandCount = 5, Seed = 31337 });
		using LocalGame second = Watching(new SessionOptions { HandCount = 5, Seed = 31337 });

		await first.PlayOutAsync();
		await second.PlayOutAsync();

		Assert.Equal(
			first.Records.Select(record => record.Seed),
			second.Records.Select(record => record.Seed));

		Assert.Equal(first.Sheet.Totals, second.Sheet.Totals);
	}

	[Fact]
	public async Task Every_hand_of_a_session_replays_from_its_own_log() {
		using LocalGame game = Watching(new SessionOptions { HandCount = 4, Seed = 606 });

		await game.PlayOutAsync();

		foreach (HandRecord record in game.Records) {
			HandState replayed = HandState.Replay(record.Events);

			Assert.Equal(GamePhase.Finished, replayed.Phase);
			Assert.Equal(record.Contract, replayed.PlayedContract);
			Assert.Equal(record.Scores, replayed.Score!.BySeat);
		}
	}

	// --- the summary ---

	[Fact]
	public async Task The_statistics_add_up() {
		using LocalGame game = Watching(new SessionOptions { HandCount = 8, Seed = 12 });

		await game.PlayOutAsync();
		SessionStats stats = game.Stats();

		Assert.Equal(8, stats.Hands);
		Assert.Equal(TarokConstants.PlayerCount, stats.Seats.Count);
		Assert.Equal(8, stats.Seats.Sum(seat => seat.Declared));
		Assert.Equal(8, stats.Contracts.Sum(tally => tally.Times));

		foreach (SeatStats seat in stats.Seats) {
			Assert.True(seat.DeclaredWon <= seat.Declared);
			Assert.True(seat.Best >= seat.Worst);
			Assert.Equal(game.Sheet.Totals[seat.Seat], seat.Total);
		}
	}

	// --- the human's seat is still the human's ---

	[Fact]
	public async Task The_bots_stop_and_wait_at_the_human_seat() {
		using LocalGame game = new("test", new SessionOptions { BotDelayMs = 0, Seed = 21 });

		await game.DealAsync();

		Assert.NotNull(game.Hand);
		Assert.NotEqual(GamePhase.Finished, game.Hand!.Phase);
		Assert.Equal(game.Seat, game.Hand.CurrentSeat);
		Assert.Null(game.Notice);
	}

	[Fact]
	public async Task An_illegal_move_is_refused_and_changes_nothing() {
		using LocalGame game = await BiddingOnYou();

		int events = game.Hand!.Events.Count;

		// It is the auction, not the play: no card can be legal yet.
		await game.PlayCardAsync(Card.Skis);

		Assert.NotNull(game.Notice);
		Assert.Equal(events, game.Hand.Events.Count);
		Assert.Equal(GamePhase.Bidding, game.Hand.Phase);
	}

	[Fact]
	public async Task The_human_can_take_any_chair_and_the_bots_stop_there() {
		using LocalGame game = new("test", new SessionOptions { BotDelayMs = 0, Seed = 21, HumanSeat = 2 });

		await game.DealAsync();

		Assert.Equal(2, game.Seat);
		Assert.Equal("Ti", game.NameOf(2));
		Assert.NotEqual("Ti", game.NameOf(0));
		Assert.Equal(2, game.Hand!.CurrentSeat);
		Assert.Equal(2, game.View!.Seat);
	}

	[Fact]
	public async Task A_finished_session_says_so_exactly_once() {
		using LocalGame game = Watching(new SessionOptions { HandCount = 2, Seed = 8 });

		int finished = 0;
		game.Finished += _ => finished++;

		await game.PlayOutAsync();

		Assert.Equal(1, finished);
	}

	// --- the registry ---

	[Fact]
	public void A_game_is_found_again_by_its_id() {
		using LocalGames games = new();

		LocalGame game = games.Create(new SessionOptions { BotDelayMs = 0 });

		Assert.Same(game, games.Find(game.Id));
		Assert.Null(games.Find("nothing"));
		Assert.Null(games.Find(null));

		games.Drop(game.Id);

		Assert.Null(games.Find(game.Id));
	}

	[Fact]
	public void Two_games_never_share_an_id() {
		using LocalGames games = new();

		string[] ids = [.. Enumerable.Range(0, 50).Select(_ => games.Create(new SessionOptions()).Id)];

		Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
	}

	// --- helpers ---

	/// <summary>A session with a bot in the human's chair and no thinking pauses.</summary>
	private static LocalGame Watching(SessionOptions options) =>
		new("test", options with { AutoPlayHuman = true, BotDelayMs = 0 });

	/// <summary>
	/// A dealt hand that is genuinely waiting for the human to bid. Most seeds do, but a deal that
	/// had to be thrown out comes back as a compulsory klop with no auction at all, so we look.
	/// </summary>
	private static async Task<LocalGame> BiddingOnYou() {
		for (int seed = 1; seed < 50; seed++) {
			LocalGame game = new("test", new SessionOptions { BotDelayMs = 0, Seed = seed });
			await game.DealAsync();

			if (game.ItIsYourTurn && game.Hand!.Phase == GamePhase.Bidding) {
				return game;
			}

			game.Dispose();
		}

		throw new InvalidOperationException("No seed in range left the human with a bid to make.");
	}
}
