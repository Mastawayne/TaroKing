namespace TaroKing.Engine.Announcing;

/// <summary>The five bonuses ("napovedi"). Each is worth more announced than taken quietly.</summary>
public enum Bonus {
	/// <summary>Trula — škis, mond and pagat all taken in tricks.</summary>
	Trula = 0,

	/// <summary>Kralji — all four kings taken in tricks.</summary>
	Kings = 1,

	/// <summary>Kralj ultimo — the last trick won with the called king.</summary>
	KingUltimo = 2,

	/// <summary>Pagat ultimo — the last trick won with the pagat.</summary>
	PagatUltimo = 3,

	/// <summary>Valat — every trick of the hand.</summary>
	Valat = 4
}

/// <param name="Bonus">Which bonus.</param>
/// <param name="SlovenianName">The word said at the table.</param>
/// <param name="SilentValue">Worth this much when it happens without being announced.</param>
/// <param name="AnnouncedValue">Worth this much when announced — and costs the same when it fails.</param>
/// <param name="HolderOnly">Only the player holding the card in question may announce it.</param>
public sealed record BonusInfo(
	Bonus Bonus,
	string SlovenianName,
	int SilentValue,
	int AnnouncedValue,
	bool HolderOnly);

public static class Bonuses {

	private static readonly BonusInfo[] Table = [
		new(Bonus.Trula, "trula", SilentValue: 10, AnnouncedValue: 20, HolderOnly: false),
		new(Bonus.Kings, "kralji", SilentValue: 10, AnnouncedValue: 20, HolderOnly: false),
		new(Bonus.KingUltimo, "kralj ultimo", SilentValue: 10, AnnouncedValue: 20, HolderOnly: true),
		new(Bonus.PagatUltimo, "pagat ultimo", SilentValue: 25, AnnouncedValue: 50, HolderOnly: true),
		new(Bonus.Valat, "valat", SilentValue: 250, AnnouncedValue: 500, HolderOnly: false)
	];

	public static IReadOnlyList<BonusInfo> All => Table;

	public static BonusInfo Info(Bonus bonus) {
		int index = (int)bonus;
		if (index < 0 || index >= Table.Length) {
			throw new ArgumentOutOfRangeException(nameof(bonus), bonus, "Unknown bonus.");
		}

		return Table[index];
	}
}
