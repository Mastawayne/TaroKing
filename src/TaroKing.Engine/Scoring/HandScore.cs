using TaroKing.Engine.Announcing;

namespace TaroKing.Engine.Scoring;

/// <summary>
/// One line of the score sheet, always written from the declaring side's point of view:
/// a positive total is points to the declaring side, a negative one points to the defenders.
/// </summary>
/// <param name="Name">What it is, in the words used at the table.</param>
/// <param name="Value">The raw value before doubling.</param>
/// <param name="Multiplier">Kontra and radlc doubling, so 1, 2, 4, 8, 16 or 32.</param>
public sealed record ScoreLine(string Name, int Value, int Multiplier = 1) {
	public int Total => Value * Multiplier;

	public override string ToString() => Multiplier == 1
		? $"{Name}: {Total:+#;-#;0}"
		: $"{Name}: {Value:+#;-#;0} ×{Multiplier} = {Total:+#;-#;0}";
}

/// <summary>What one hand did to everybody's score.</summary>
public sealed class HandScore {

	private readonly int[] _bySeat;
	private readonly List<ScoreLine> _lines;

	internal HandScore(IReadOnlyList<ScoreLine> lines, int[] bySeat, int cardPoints, int difference, bool declarerWon) {
		_lines = [.. lines];
		_bySeat = bySeat;
		CardPoints = cardPoints;
		Difference = difference;
		DeclarerWon = declarerWon;
	}

	/// <summary>Every item that made up the result, in the order it belongs on the sheet.</summary>
	public IReadOnlyList<ScoreLine> Lines => _lines;

	/// <summary>Card points taken by the declaring side. 0 in klop, where everybody counts separately.</summary>
	public int CardPoints { get; }

	/// <summary>The rounded margin over 35, or 0 in contracts that do not score one.</summary>
	public int Difference { get; }

	public bool DeclarerWon { get; }

	/// <summary>The declaring side's net for the hand, before personal penalties.</summary>
	public int DeclaringTotal => _lines.Sum(line => line.Total);

	/// <summary>What one seat writes down, penalties included.</summary>
	public int this[int seat] {
		get {
			ArgumentOutOfRangeException.ThrowIfNegative(seat);
			ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(seat, TarokConstants.PlayerCount);
			return _bySeat[seat];
		}
	}

	public IReadOnlyList<int> BySeat => _bySeat;
}

/// <summary>Which side took a bonus, if either did.</summary>
public readonly record struct BonusOutcome(Bonus Kind, int? AchievedBySide);
