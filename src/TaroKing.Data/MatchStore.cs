using Microsoft.EntityFrameworkCore;

using TaroKing.Engine.Bidding;
using TaroKing.Engine.Session;

namespace TaroKing.Data;

/// <summary>A match as the history page lists it.</summary>
public sealed record MatchSummary(
	int Id,
	MatchKind Kind,
	string Name,
	int Rounds,
	bool Rated,
	DateTimeOffset FinishedAt,
	int YourSeat,
	int YourFinal,
	int? YourRatingDelta,
	IReadOnlyList<string> Names);

/// <summary>One line of the leaderboard.</summary>
public sealed record LeaderboardEntry(int Place, string UserId, string Name, int Rating, int RatedMatches, int MatchesPlayed);

/// <summary>How a player does with one contract when they declare it.</summary>
public sealed record ContractRecord(Contract Contract, string Name, int Played, int Won) {
	public double SuccessRate => Played == 0 ? 0 : Won / (double)Played;
}

/// <summary>A player's numbers, from every hand they ever sat through.</summary>
public sealed record PlayerStats(
	int MatchesPlayed,
	int HandsPlayed,
	int HandsDeclared,
	int HandsDeclaredWon,
	double AverageDifference,
	int PagatUltimoAnnounced,
	int PagatUltimoConverted,
	IReadOnlyList<ContractRecord> Contracts) {

	public double DeclarerSuccessRate => HandsDeclared == 0 ? 0 : HandsDeclaredWon / (double)HandsDeclared;

	public double PagatUltimoConversion => PagatUltimoAnnounced == 0 ? 0 : PagatUltimoConverted / (double)PagatUltimoAnnounced;
}

/// <summary>What the lobby shows next to a name.</summary>
public sealed record PlayerFlags(string UserId, string Name, int Rating, bool IsMember, bool IsFrequentLeaver, int Reports);

/// <summary>
/// Everything the game writes to and reads from the database about matches and players.
///
/// It takes a context factory rather than a context: a Blazor circuit lives for as long as the
/// tab is open, and a context that lived that long would drag every match it ever touched along
/// with it. Each call here opens a context, does its work and closes it again.
/// </summary>
public sealed class MatchStore(IDbContextFactory<TaroKingDbContext> factory) {

	/// <summary>The lobby's "last 30 games".</summary>
	public const int HistoryLength = 30;

	// --- writing a match down ---

	/// <summary>
	/// Store a finished match, settle the ratings if at least two registered players sat at it,
	/// and bump everybody's counters. Returns the new match id.
	/// </summary>
	public async Task<int> SaveAsync(FinishedMatch finished, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(finished);

		await using TaroKingDbContext db = await factory.CreateDbContextAsync(cancellationToken);

		// A retried archive must never write the same table twice: the first write wins.
		int existing = await db.Matches
			.Where(m => m.Kind == finished.Kind && m.SourceId == finished.SourceId)
			.Select(m => m.Id)
			.FirstOrDefaultAsync(cancellationToken);

		if (existing != 0) {
			return existing;
		}

		Match match = new() {
			Kind = finished.Kind,
			SourceId = finished.SourceId,
			Name = finished.Name,
			Rounds = finished.Rounds,
			StartedAt = finished.StartedAt,
			FinishedAt = finished.FinishedAt
		};

		foreach (FinishedSeat seat in finished.Seats) {
			match.Seats.Add(new MatchSeat {
				Seat = seat.Seat,
				UserId = seat.UserId,
				Name = seat.Name,
				IsBot = seat.IsBot,
				Abandoned = seat.Abandoned,
				Total = seat.Total,
				FinalTotal = seat.FinalTotal,
				Radlci = seat.Radlci
			});
		}

		foreach (FinishedHand hand in finished.Hands) {
			Hand stored = new() {
				Number = hand.Number,
				Seed = hand.Seed,
				CompulsoryKlop = hand.CompulsoryKlop,
				Contract = hand.Contract,
				Declarer = hand.Declarer,
				DeclarerWon = hand.DeclarerWon,
				CardPoints = hand.CardPoints,
				Difference = hand.Difference,
				PagatUltimoAnnouncedBy = hand.PagatUltimoAnnouncedBy,
				PagatUltimoWonBy = hand.PagatUltimoWonBy
			};

			stored.SetScores(hand.Scores);

			int ordinal = 0;
			foreach (EncodedEvent encoded in EventCodec.EncodeAll(hand.Events)) {
				stored.Events.Add(new HandEvent {
					Ordinal = ordinal++,
					Kind = encoded.Kind,
					Seat = encoded.Seat,
					Payload = encoded.Payload
				});
			}

			match.Hands.Add(stored);
		}

		foreach (FinishedChat line in finished.Chat) {
			match.Chat.Add(new ChatMessage {
				At = line.At,
				Seat = line.Seat,
				Who = line.Who,
				Text = line.Text,
				FromTable = line.FromTable
			});
		}

		// The people, as they are right now. A deleted account is nobody: its seat stays, unrated.
		string[] userIds = [.. finished.Seats.Where(seat => seat.UserId is not null).Select(seat => seat.UserId!).Distinct()];
		Dictionary<string, TaroKingUser> users = await db.Users
			.Where(user => userIds.Contains(user.Id) && !user.IsDeleted)
			.ToDictionaryAsync(user => user.Id, cancellationToken);

		match.Rated = users.Count >= 2 && finished.Kind == MatchKind.Online;

		if (match.Rated) {
			List<RatedSeat> rated = [.. finished.Seats
				.Where(seat => seat.UserId is not null && users.ContainsKey(seat.UserId))
				.Select(seat => new RatedSeat(seat.Seat, users[seat.UserId!].Rating, seat.FinalTotal))];

			IReadOnlyDictionary<int, int> deltas = Rating.Deltas(rated, finished.Rounds);

			foreach (MatchSeat seat in match.Seats) {
				if (seat.UserId is null || !users.TryGetValue(seat.UserId, out TaroKingUser? user) || !deltas.TryGetValue(seat.Seat, out int delta)) {
					continue;
				}

				seat.RatingBefore = user.Rating;
				seat.RatingAfter = user.Rating + delta;

				db.Ratings.Add(new RatingChange {
					User = user,
					Match = match,
					Before = user.Rating,
					After = user.Rating + delta,
					At = finished.FinishedAt
				});

				user.Rating += delta;
				user.RatedMatches++;
			}
		}

		foreach (FinishedSeat seat in finished.Seats) {
			if (seat.UserId is not null && users.TryGetValue(seat.UserId, out TaroKingUser? user)) {
				user.MatchesPlayed++;

				if (seat.Abandoned) {
					user.MatchesAbandoned++;
				}
			}
		}

		db.Matches.Add(match);
		await db.SaveChangesAsync(cancellationToken);

		return match.Id;
	}

	// --- reading it back ---

	/// <summary>The player's most recent matches, newest first.</summary>
	public async Task<IReadOnlyList<MatchSummary>> HistoryAsync(string userId, int take = HistoryLength, CancellationToken cancellationToken = default) {
		await using TaroKingDbContext db = await factory.CreateDbContextAsync(cancellationToken);

		List<Match> matches = await db.Matches
			.AsNoTracking()
			.Where(match => match.Seats.Any(seat => seat.UserId == userId))
			.OrderByDescending(match => match.FinishedAt)
			.Take(take)
			.Include(match => match.Seats)
			.ToListAsync(cancellationToken);

		return [.. matches.Select(match => Summarise(match, userId))];
	}

	public async Task<Match?> MatchAsync(int id, CancellationToken cancellationToken = default) {
		await using TaroKingDbContext db = await factory.CreateDbContextAsync(cancellationToken);

		return await db.Matches
			.AsNoTracking()
			.Include(match => match.Seats)
			.Include(match => match.Hands.OrderBy(hand => hand.Number))
			.Include(match => match.Chat.OrderBy(line => line.At))
			.FirstOrDefaultAsync(match => match.Id == id, cancellationToken);
	}

	/// <summary>One hand's log, decoded, so it can be replayed.</summary>
	public async Task<IReadOnlyList<GameEvent>> EventsAsync(int handId, CancellationToken cancellationToken = default) {
		await using TaroKingDbContext db = await factory.CreateDbContextAsync(cancellationToken);

		List<HandEvent> lines = await db.Events
			.AsNoTracking()
			.Where(line => line.HandId == handId)
			.OrderBy(line => line.Ordinal)
			.ToListAsync(cancellationToken);

		return EventCodec.DecodeAll(lines.Select(line => new EncodedEvent(line.Kind, line.Seat, line.Payload)));
	}

	public async Task<IReadOnlyList<LeaderboardEntry>> LeaderboardAsync(int take = 50, CancellationToken cancellationToken = default) {
		await using TaroKingDbContext db = await factory.CreateDbContextAsync(cancellationToken);

		List<TaroKingUser> users = await db.Users
			.AsNoTracking()
			.Where(user => user.RatedMatches > 0)
			.OrderByDescending(user => user.Rating)
			.ThenByDescending(user => user.RatedMatches)
			.ThenBy(user => user.UserName)
			.Take(take)
			.ToListAsync(cancellationToken);

		return [.. users.Select((user, index) =>
			new LeaderboardEntry(index + 1, user.Id, user.UserName ?? "?", user.Rating, user.RatedMatches, user.MatchesPlayed))];
	}

	/// <summary>Success per contract, average difference, pagat ultimo conversion — from every hand they sat through.</summary>
	public async Task<PlayerStats> StatsAsync(string userId, CancellationToken cancellationToken = default) {
		await using TaroKingDbContext db = await factory.CreateDbContextAsync(cancellationToken);

		var seats = await db.MatchSeats
			.AsNoTracking()
			.Where(seat => seat.UserId == userId)
			.Select(seat => new { seat.MatchId, seat.Seat })
			.ToListAsync(cancellationToken);

		if (seats.Count == 0) {
			return new PlayerStats(0, 0, 0, 0, 0, 0, 0, []);
		}

		int[] matchIds = [.. seats.Select(seat => seat.MatchId)];
		Dictionary<int, int> seatByMatch = seats.ToDictionary(seat => seat.MatchId, seat => seat.Seat);

		List<Hand> hands = await db.Hands
			.AsNoTracking()
			.Where(hand => matchIds.Contains(hand.MatchId))
			.ToListAsync(cancellationToken);

		int declared = 0;
		int declaredWon = 0;
		int differenceHands = 0;
		long differenceSum = 0;
		int announced = 0;
		int converted = 0;
		Dictionary<Contract, (int Played, int Won)> perContract = [];

		foreach (Hand hand in hands) {
			int mySeat = seatByMatch[hand.MatchId];
			ContractInfo info = Contracts.Info(hand.Contract);

			if (hand.Declarer == mySeat && !info.IsKlop) {
				declared++;

				if (hand.DeclarerWon) {
					declaredWon++;
				}

				(int played, int won) = perContract.GetValueOrDefault(hand.Contract);
				perContract[hand.Contract] = (played + 1, won + (hand.DeclarerWon ? 1 : 0));

				if (info.ScoresDifference) {
					differenceHands++;
					differenceSum += hand.Difference;
				}
			}

			if (hand.PagatUltimoAnnouncedBy == mySeat) {
				announced++;

				if (hand.PagatUltimoWonBy == mySeat) {
					converted++;
				}
			}
		}

		List<ContractRecord> contracts = [.. perContract
			.Select(pair => new ContractRecord(pair.Key, Contracts.Info(pair.Key).SlovenianName, pair.Value.Played, pair.Value.Won))
			.OrderBy(record => record.Contract)];

		return new PlayerStats(
			MatchesPlayed: seats.Count,
			HandsPlayed: hands.Count,
			HandsDeclared: declared,
			HandsDeclaredWon: declaredWon,
			AverageDifference: differenceHands == 0 ? 0 : differenceSum / (double)differenceHands,
			PagatUltimoAnnounced: announced,
			PagatUltimoConverted: converted,
			Contracts: contracts);
	}

	/// <summary>The lobby's badges for a set of players.</summary>
	public async Task<IReadOnlyDictionary<string, PlayerFlags>> FlagsAsync(IEnumerable<string> userIds, CancellationToken cancellationToken = default) {
		string[] ids = [.. userIds.Distinct()];

		if (ids.Length == 0) {
			return new Dictionary<string, PlayerFlags>();
		}

		await using TaroKingDbContext db = await factory.CreateDbContextAsync(cancellationToken);

		List<TaroKingUser> users = await db.Users
			.AsNoTracking()
			.Where(user => ids.Contains(user.Id))
			.ToListAsync(cancellationToken);

		var reports = await db.Reports
			.AsNoTracking()
			.Where(report => ids.Contains(report.ReportedId))
			.GroupBy(report => report.ReportedId)
			.Select(group => new { UserId = group.Key, Count = group.Count() })
			.ToDictionaryAsync(entry => entry.UserId, entry => entry.Count, cancellationToken);

		return users.ToDictionary(
			user => user.Id,
			user => new PlayerFlags(
				user.Id,
				user.UserName ?? "?",
				user.Rating,
				IsMember: true,
				user.IsFrequentLeaver,
				reports.GetValueOrDefault(user.Id)));
	}

	/// <summary>Add a report, once per reporter per reported. Returns false if it already existed.</summary>
	public async Task<bool> ReportAsync(string reporterId, string reportedId, string reason, CancellationToken cancellationToken = default) {
		if (reporterId == reportedId) {
			return false;
		}

		await using TaroKingDbContext db = await factory.CreateDbContextAsync(cancellationToken);

		bool exists = await db.Reports.AnyAsync(
			report => report.ReporterId == reporterId && report.ReportedId == reportedId,
			cancellationToken);

		if (exists) {
			return false;
		}

		string trimmed = (reason ?? "").Trim();

		db.Reports.Add(new BlacklistReport {
			ReporterId = reporterId,
			ReportedId = reportedId,
			Reason = trimmed[..Math.Min(trimmed.Length, 200)],
			At = DateTimeOffset.UtcNow
		});

		await db.SaveChangesAsync(cancellationToken);

		return true;
	}

	public async Task<TaroKingUser?> UserAsync(string userId, CancellationToken cancellationToken = default) {
		await using TaroKingDbContext db = await factory.CreateDbContextAsync(cancellationToken);

		return await db.Users.AsNoTracking().FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);
	}

	private static MatchSummary Summarise(Match match, string userId) {
		MatchSeat mine = match.Seats.First(seat => seat.UserId == userId);

		return new MatchSummary(
			Id: match.Id,
			Kind: match.Kind,
			Name: match.Name,
			Rounds: match.Rounds,
			Rated: match.Rated,
			FinishedAt: match.FinishedAt,
			YourSeat: mine.Seat,
			YourFinal: mine.FinalTotal,
			YourRatingDelta: match.Rated && mine.RatingAfter is not null ? mine.RatingDelta : null,
			Names: [.. match.Seats.OrderBy(seat => seat.Seat).Select(seat => seat.Name)]);
	}
}
