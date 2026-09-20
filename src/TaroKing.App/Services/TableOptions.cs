using TaroKing.Engine;

namespace TaroKing.App.Services;

/// <summary>Who may sit down and who may even see the table in the lobby.</summary>
public enum TableVisibility {
	/// <summary>Listed in the lobby, anybody may take a seat.</summary>
	Public = 0,

	/// <summary>Not listed; you need the link.</summary>
	Private = 1
}

/// <summary>
/// What a host chooses when opening a table, in the shape valat.si offers.
///
/// <see cref="SecondsPerMove"/> is an increment, not a guillotine: each move gives the seat that
/// many seconds back, and anything longer eats into <see cref="ReserveSeconds"/>. A hard 1.5-second
/// limit would be unplayable for a human, and a chess clock is the only reading of "sekunde na
/// potezo" that makes the range sensible — worth checking against a real valat.si table.
/// </summary>
public sealed record TableOptions {

	public const int MinRounds = 7;
	public const int MaxRounds = 30;
	public const double MinSecondsPerMove = 1.5;
	public const double MaxSecondsPerMove = 4.5;

	/// <summary>What the table is called in the lobby.</summary>
	public string Name { get; init; } = "Miza";

	public TableVisibility Visibility { get; init; } = TableVisibility.Public;

	/// <summary>Hands to play before the table is finished. 7 to 30, as in the lobby.</summary>
	public int Rounds { get; init; } = 12;

	/// <summary>Seconds added to a seat's clock for every move it makes.</summary>
	public double SecondsPerMove { get; init; } = 3.0;

	/// <summary>The bank a seat starts with and spends when it thinks longer than the increment.</summary>
	public int ReserveSeconds { get; init; } = 60;

	/// <summary>Nobody rated below this may sit down. Ratings are placeholder until accounts land.</summary>
	public int MinimumRating { get; init; }

	/// <summary>Registered players only. Inert until there are accounts to be a member of.</summary>
	public bool MembersOnly { get; init; }

	/// <summary>Fill the empty seats with bots so the hand can start before four people arrive.</summary>
	public bool FillWithBots { get; init; } = true;

	/// <summary>
	/// Seats at the table. Slovenian tarok is also played three-handed, but the engine is strictly
	/// four-player, so this is fixed until the three-handed rules are built.
	/// </summary>
	public int Seats => TarokConstants.PlayerCount;

	/// <summary>Pull anything out of range back into it rather than refusing the table.</summary>
	public TableOptions Clamped() => this with {
		Name = string.IsNullOrWhiteSpace(Name) ? "Miza" : Name.Trim()[..Math.Min(Name.Trim().Length, 40)],
		Rounds = Math.Clamp(Rounds, MinRounds, MaxRounds),
		SecondsPerMove = Math.Clamp(SecondsPerMove, MinSecondsPerMove, MaxSecondsPerMove),
		ReserveSeconds = Math.Clamp(ReserveSeconds, 15, 300),
		MinimumRating = Math.Max(0, MinimumRating)
	};

	public string Describe() =>
		$"{Rounds} partij · {SecondsPerMove.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} s na potezo · rezerva {ReserveSeconds} s";
}
