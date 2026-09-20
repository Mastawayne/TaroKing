using TaroKing.Data;
using TaroKing.Engine;
using TaroKing.Engine.Session;

namespace TaroKing.App.Services;

/// <summary>
/// Turns finished games into <see cref="FinishedMatch"/>es and queues them for the history. A
/// table or a session raises its "finished" once; the <see cref="ArchiveWorker"/> writes the match
/// with retries and settles the ratings. A guest's game against bots has nobody to remember it
/// for and is skipped. Nothing here touches the database, so it is safe to call from a heartbeat.
/// </summary>
public sealed class MatchArchiver(ArchiveQueue queue, ILogger<MatchArchiver> log) {

	/// <summary>Queue an online table. Every seat goes in, member or guest or bot, so the history reads right.</summary>
	public bool Enqueue(OnlineTable table) {
		ArgumentNullException.ThrowIfNull(table);

		if (table.Phase != TablePhase.Finished || table.Records.Count == 0 || table.Faulted) {
			return false;
		}

		FinishedMatch finished = Distil(table);
		log.LogInformation("Queued online match {SourceId} for the archive.", finished.SourceId);

		return queue.Enqueue(finished, table.Id);
	}

	/// <summary>What the history will hold for this table.</summary>
	public static FinishedMatch Distil(OnlineTable table) {
		ArgumentNullException.ThrowIfNull(table);

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

		return new FinishedMatch(
			Kind: MatchKind.Online,
			SourceId: table.Id,
			Name: table.Options.Name,
			Rounds: table.Options.Rounds,
			StartedAt: table.StartedAt ?? table.LastActivity,
			FinishedAt: DateTimeOffset.UtcNow,
			Seats: seats,
			Hands: Distil(table.Records),
			Chat: [.. table.Chat.Select(line => new FinishedChat(line.At, line.Seat, line.Who, line.Text, line.FromTable))]);
	}

	/// <summary>Queue a session against bots, if a member played it.</summary>
	public bool Enqueue(LocalGame game) {
		ArgumentNullException.ThrowIfNull(game);

		if (!game.IsOver || game.Records.Count == 0 || !game.Owner.IsMember || game.Options.IsReplay) {
			return false;
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

		return queue.Enqueue(finished, null);
	}

	/// <summary>For the local-games callback: queue and forget.</summary>
	public void ArchiveLater(LocalGame game) {
		try {
			Enqueue(game);
		} catch (Exception error) {
			log.LogError(error, "Could not queue local game {GameId}.", game.Id);
		}
	}

	/// <summary>The stored hands need a few numbers the record does not carry; replaying the log gives them.</summary>
	private static IReadOnlyList<FinishedHand> Distil(IReadOnlyList<HandRecord> records) =>
		[.. records.Select(record => FinishedHand.From(HandState.Replay(record.Events), record.Number))];
}
