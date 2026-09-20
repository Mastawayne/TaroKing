using TaroKing.Data;
using TaroKing.Engine;
using TaroKing.Engine.Session;

namespace TaroKing.App.Services;

/// <summary>
/// Writes finished games into the database. A table or a session raises its "finished" once; this
/// turns what it knows into a <see cref="FinishedMatch"/> and hands it to the store, which settles
/// the ratings. A guest's game against bots has nobody to remember it for and is skipped.
/// </summary>
public sealed class MatchArchiver(MatchStore store, ILogger<MatchArchiver> log) {

	/// <summary>Store an online table. Every seat goes in, member or guest or bot, so the history reads right.</summary>
	public async Task<int?> ArchiveAsync(OnlineTable table, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(table);

		if (table.Phase != TablePhase.Finished || table.Records.Count == 0) {
			return null;
		}

		IReadOnlyList<int> finals = table.Sheet.FinalTotals();

		List<FinishedSeat> seats = [];
		for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
			TableSeat chair = table.Seats[seat];

			seats.Add(new FinishedSeat(
				Seat: seat,
				UserId: chair.Player?.UserId,
				Name: chair.Name,
				IsBot: chair.Player is null,
				Abandoned: chair.Player is not null && (chair.Abandoned || chair.BotIsPlaying),
				Total: table.Sheet.Totals[seat],
				FinalTotal: finals[seat],
				Radlci: table.Sheet.Radlci[seat]));
		}

		FinishedMatch finished = new(
			Kind: MatchKind.Online,
			SourceId: table.Id,
			Name: table.Options.Name,
			Rounds: table.Options.Rounds,
			StartedAt: table.StartedAt ?? table.LastActivity,
			FinishedAt: DateTimeOffset.UtcNow,
			Seats: seats,
			Hands: Distil(table.Records),
			Chat: [.. table.Chat.Select(line => new FinishedChat(line.At, line.Seat, line.Who, line.Text, line.FromTable))]);

		return await SaveAsync(finished, cancellationToken);
	}

	/// <summary>Store a session against bots, if a member played it.</summary>
	public async Task<int?> ArchiveAsync(LocalGame game, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(game);

		if (!game.IsOver || game.Records.Count == 0 || !game.Owner.IsMember || game.Options.IsReplay) {
			return null;
		}

		IReadOnlyList<int> finals = game.Sheet.FinalTotals();

		List<FinishedSeat> seats = [];
		for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
			bool human = seat == game.Seat;

			seats.Add(new FinishedSeat(
				Seat: seat,
				UserId: human ? game.Owner.UserId : null,
				Name: human ? game.Owner.Name : game.NameOf(seat),
				IsBot: !human,
				Abandoned: false,
				Total: game.Sheet.Totals[seat],
				FinalTotal: finals[seat],
				Radlci: game.Sheet.Radlci[seat]));
		}

		FinishedMatch finished = new(
			Kind: MatchKind.Local,
			SourceId: game.Id,
			Name: $"proti botom · {game.Options.Describe()}",
			Rounds: game.Records.Count,
			StartedAt: game.StartedAt,
			FinishedAt: DateTimeOffset.UtcNow,
			Seats: seats,
			Hands: Distil(game.Records),
			Chat: []);

		return await SaveAsync(finished, cancellationToken);
	}

	/// <summary>Archive without waiting, for callers on a hot path. Failures are logged, never thrown.</summary>
	public void ArchiveLater(LocalGame game) {
		_ = Task.Run(async () => {
			try {
				await ArchiveAsync(game);
			} catch (Exception error) {
				log.LogError(error, "Could not archive local game {GameId}.", game.Id);
			}
		});
	}

	/// <summary>The stored hands need a few numbers the record does not carry; replaying the log gives them.</summary>
	private static IReadOnlyList<FinishedHand> Distil(IReadOnlyList<HandRecord> records) =>
		[.. records.Select(record => FinishedHand.From(HandState.Replay(record.Events), record.Number))];

	private async Task<int?> SaveAsync(FinishedMatch finished, CancellationToken cancellationToken) {
		try {
			int id = await store.SaveAsync(finished, cancellationToken);
			log.LogInformation("Archived {Kind} match {SourceId} as #{MatchId}.", finished.Kind, finished.SourceId, id);

			return id;
		} catch (Exception error) {
			log.LogError(error, "Could not archive {Kind} match {SourceId}.", finished.Kind, finished.SourceId);

			return null;
		}
	}
}
