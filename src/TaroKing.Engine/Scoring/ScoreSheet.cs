using TaroKing.Engine.Bidding;

namespace TaroKing.Engine.Scoring;

/// <summary>
/// The running sheet for a session: everybody's total, everybody's radlci, and the hands so far.
///
/// A radlc ("wheel") is written for all four players whenever a klop is played, whenever a contract
/// of berač or higher is played, and whenever a valat is won or lost. While you carry one, the hand
/// you declare counts double; win it and one of your radlci is crossed off, lose it and it stays.
/// Whatever is still uncancelled at the end of the session costs 100 points each.
/// </summary>
public sealed class ScoreSheet {

	private readonly List<HandScore> _hands = [];
	private readonly int[] _radlci = new int[TarokConstants.PlayerCount];
	private readonly int[] _totals = new int[TarokConstants.PlayerCount];

	/// <summary>Running totals, before the radlci are settled.</summary>
	public IReadOnlyList<int> Totals => _totals;

	/// <summary>Uncancelled radlci per seat.</summary>
	public IReadOnlyList<int> Radlci => _radlci;

	public IReadOnlyList<HandScore> Hands => _hands;

	/// <summary>2 while this seat carries a radlc, which doubles the hand they declare.</summary>
	public int RadlcMultiplierFor(int seat) {
		ValidateSeat(seat);
		return _radlci[seat] > 0 ? 2 : 1;
	}

	/// <summary>Write a finished hand onto the sheet and settle the radlci it touches.</summary>
	public void Record(Contract contract, int declarerSeat, HandScore score, bool valatWasInPlay = false) {
		ArgumentNullException.ThrowIfNull(score);
		ValidateSeat(declarerSeat);

		for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
			_totals[seat] += score[seat];
		}

		_hands.Add(score);

		ContractInfo info = Contracts.Info(contract);

		// Winning a hand you declared while carrying a radlc crosses one off.
		if (score.DeclarerWon && !info.IsKlop && _radlci[declarerSeat] > 0) {
			_radlci[declarerSeat]--;
		}

		if (WritesNewRadlci(info, valatWasInPlay)) {
			for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
				_radlci[seat]++;
			}
		}
	}

	/// <summary>Totals with the uncancelled radlci charged at 100 each.</summary>
	public IReadOnlyList<int> FinalTotals() {
		int[] final = new int[TarokConstants.PlayerCount];

		for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
			final[seat] = _totals[seat] + (_radlci[seat] * TarokConstants.UncancelledRadlcPenalty);
		}

		return final;
	}

	private static bool WritesNewRadlci(ContractInfo info, bool valatWasInPlay) =>
		info.IsKlop || info.Contract >= Contract.Beggar || valatWasInPlay;

	private static void ValidateSeat(int seat) {
		ArgumentOutOfRangeException.ThrowIfNegative(seat);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(seat, TarokConstants.PlayerCount);
	}
}
