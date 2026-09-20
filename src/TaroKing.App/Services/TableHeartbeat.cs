using System.Diagnostics;

using TaroKing.Data;

namespace TaroKing.App.Services;

/// <summary>
/// The clock every online table runs on. Nothing at a table happens because a browser asked for it:
/// bots move, clocks run out and abandoned seats change hands on this beat, whether or not anybody
/// is looking. That is what makes the server the authority rather than the fastest client.
///
/// Before the first beat it puts back every table the journal remembers. When a table finishes it
/// is queued for the archive — the write itself never runs on this thread, so a slow database
/// cannot stop the clocks.
/// </summary>
public sealed class TableHeartbeat(TableService tables, MatchArchiver archiver, LiveTableStore live, ServerMetrics metrics, IHostApplicationLifetime lifetime, ILogger<TableHeartbeat> log) : BackgroundService {

	private static readonly TimeSpan Beat = TimeSpan.FromMilliseconds(250);
	private static readonly TimeSpan SweepEvery = TimeSpan.FromMinutes(5);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
		try {
			int restored = await tables.RestoreAsync(live, archiver, log, stoppingToken);
			if (restored > 0) {
				log.LogInformation("Restored {Count} live tables from the journal.", restored);
			}
		} catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
			return;
		} catch (Exception error) {
			log.LogError(error, "Could not restore live tables; starting with none.");
		}

		lifetime.ApplicationStopping.Register(tables.BeginShutdown);

		using PeriodicTimer timer = new(Beat);
		DateTimeOffset nextSweep = DateTimeOffset.UtcNow + SweepEvery;

		while (await timer.WaitForNextTickAsync(stoppingToken)) {
			DateTimeOffset now = DateTimeOffset.UtcNow;
			long started = Stopwatch.GetTimestamp();

			foreach (OnlineTable table in tables.All) {
				try {
					int before = table.HandNumber;
					await table.TickAsync(now);

					if (table.HandNumber > before) {
						metrics.HandDealt();
					}

					if (table.Phase == TablePhase.Finished && !table.Archived) {
						// Queued once; the worker retries, and the journal keeps the table until it lands.
						table.Archived = archiver.Enqueue(table) || table.Faulted;
					}
				} catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
					return;
				} catch (Exception error) {
					// One sick table must never stop the others.
					log.LogError(error, "Table {TableId} threw on a heartbeat.", table.Id);
				}
			}

			metrics.TickTook(Stopwatch.GetElapsedTime(started));

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
