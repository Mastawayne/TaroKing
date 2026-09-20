namespace TaroKing.App.Services;

/// <summary>
/// The clock every online table runs on. Nothing at a table happens because a browser asked for it:
/// bots move, clocks run out and abandoned seats change hands on this beat, whether or not anybody
/// is looking. That is what makes the server the authority rather than the fastest client.
///
/// It is also where a finished table gets written down, exactly once.
/// </summary>
public sealed class TableHeartbeat(TableService tables, MatchArchiver archiver, ILogger<TableHeartbeat> log) : BackgroundService {

	private static readonly TimeSpan Beat = TimeSpan.FromMilliseconds(250);
	private static readonly TimeSpan SweepEvery = TimeSpan.FromMinutes(5);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
		using PeriodicTimer timer = new(Beat);
		DateTimeOffset nextSweep = DateTimeOffset.UtcNow + SweepEvery;

		while (await timer.WaitForNextTickAsync(stoppingToken)) {
			DateTimeOffset now = DateTimeOffset.UtcNow;

			foreach (OnlineTable table in tables.All) {
				try {
					await table.TickAsync(now);

					if (table.Phase == TablePhase.Finished && !table.Archived) {
						table.Archived = true;
						await archiver.ArchiveAsync(table, stoppingToken);
					}
				} catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
					return;
				} catch (Exception error) {
					// One sick table must never stop the others.
					log.LogError(error, "Table {TableId} threw on a heartbeat.", table.Id);
				}
			}

			if (now >= nextSweep) {
				nextSweep = now + SweepEvery;

				int closed = tables.Sweep(now);
				if (closed > 0) {
					log.LogInformation("Closed {Count} idle tables.", closed);
				}
			}
		}
	}
}
