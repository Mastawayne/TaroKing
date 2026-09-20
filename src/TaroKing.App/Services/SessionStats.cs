using TaroKing.Engine;
using TaroKing.Engine.Bidding;
using TaroKing.Engine.Scoring;
using TaroKing.Engine.Session;

namespace TaroKing.App.Services;

/// <summary>One finished hand, kept so a session can be summarised and, later, reviewed.</summary>
public sealed record HandRecord(
	int Number,
	int Seed,
	Contract Contract,
	int Declarer,
	bool DeclarerWon,
	IReadOnlyList<int> Scores,
	IReadOnlyList<GameEvent> Events);

/// <summary>One player's session at a glance.</summary>
public sealed record SeatStats(
	int Seat,
	string Name,
	int Total,
	int Final,
	int Radlci,
	int Declared,
	int DeclaredWon,
	int Best,
	int Worst);

public sealed record ContractTally(Contract Contract, string Name, int Times);

public sealed record SessionStats(
	int Hands,
	IReadOnlyList<SeatStats> Seats,
	IReadOnlyList<ContractTally> Contracts);

/// <summary>Turns a pile of finished hands into the numbers a player wants at the end.</summary>
public static class SessionSummary {

	public static SessionStats Build(IReadOnlyList<HandRecord> records, ScoreSheet sheet, Func<int, string> nameOf) {
		ArgumentNullException.ThrowIfNull(records);
		ArgumentNullException.ThrowIfNull(sheet);
		ArgumentNullException.ThrowIfNull(nameOf);

		List<SeatStats> seats = [];
		IReadOnlyList<int> finals = sheet.FinalTotals();

		for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
			int current = seat;
			int[] mine = [.. records.Select(record => record.Scores[current])];

			seats.Add(new SeatStats(
				Seat: seat,
				Name: nameOf(seat),
				Total: sheet.Totals[seat],
				Final: finals[seat],
				Radlci: sheet.Radlci[seat],
				Declared: records.Count(record => record.Declarer == current),
				DeclaredWon: records.Count(record => record.Declarer == current && record.DeclarerWon),
				Best: mine.Length == 0 ? 0 : mine.Max(),
				Worst: mine.Length == 0 ? 0 : mine.Min()));
		}

		List<ContractTally> contracts = [.. records
			.GroupBy(record => record.Contract)
			.Select(group => new ContractTally(group.Key, Contracts.Info(group.Key).SlovenianName, group.Count()))
			.OrderByDescending(tally => tally.Times)
			.ThenBy(tally => tally.Contract)];

		return new SessionStats(records.Count, seats, contracts);
	}
}
