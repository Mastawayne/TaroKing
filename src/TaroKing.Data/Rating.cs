namespace TaroKing.Data;

/// <summary>One rated seat going into a match.</summary>
public readonly record struct RatedSeat(int Seat, int Rating, int FinalTotal);

/// <summary>
/// The rating, in the shape valat.si describes: Elo-like, settled once per match rather than per
/// trick, weighted by how long the match was, by the ratio of the final scores, and by the
/// opponents' ratings going in.
///
/// Every pair of rated players at the table is scored against each other. The "result" of a pair
/// is not win-or-lose but how far apart they finished, so beating somebody by 300 on the sheet
/// moves more than beating them by 5, and a long match moves more than a short one. Bots and
/// guests have no rating and are simply not in the pairing; with fewer than two rated players the
/// match is unrated.
/// </summary>
public static class Rating {

	/// <summary>How far a 12-round match can move one player against one opponent.</summary>
	public const double BaseK = 24;

	/// <summary>The match length that counts as "normal"; longer moves more, shorter less.</summary>
	public const int ReferenceRounds = 12;

	/// <summary>Keeps a two-hand match from swinging on a single klop.</summary>
	private const double ScoreDamping = 50;

	public static IReadOnlyDictionary<int, int> Deltas(IReadOnlyList<RatedSeat> seats, int rounds) {
		ArgumentNullException.ThrowIfNull(seats);

		Dictionary<int, int> deltas = [];

		if (seats.Count < 2) {
			return deltas;
		}

		double k = BaseK * Math.Sqrt(Math.Max(rounds, 1) / (double)ReferenceRounds);

		foreach (RatedSeat me in seats) {
			double sum = 0;

			foreach (RatedSeat other in seats) {
				if (other.Seat == me.Seat) {
					continue;
				}

				sum += Result(me.FinalTotal, other.FinalTotal) - Expected(me.Rating, other.Rating);
			}

			deltas[me.Seat] = (int)Math.Round(k * sum / (seats.Count - 1), MidpointRounding.AwayFromZero);
		}

		return deltas;
	}

	/// <summary>The classic Elo expectation on a 400-point scale.</summary>
	public static double Expected(int mine, int theirs) =>
		1.0 / (1.0 + Math.Pow(10, (theirs - mine) / 400.0));

	/// <summary>
	/// 1 for a clear win, 0 for a clear loss, and everything in between by the ratio of the scores:
	/// finishing +200 against somebody's −200 is nearly 1, +40 against +30 is barely over half.
	/// </summary>
	public static double Result(int mine, int theirs) {
		double spread = Math.Abs(mine) + Math.Abs(theirs) + ScoreDamping;
		double margin = (mine - theirs) / spread;

		return Math.Clamp(0.5 + (0.5 * margin), 0, 1);
	}
}
