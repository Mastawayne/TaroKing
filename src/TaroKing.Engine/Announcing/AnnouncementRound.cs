using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;

namespace TaroKing.Engine.Announcing;

/// <summary>Which side of the table a seat plays for.</summary>
public static class Sides {

	/// <summary>The declarer and, in a called-king contract, the partner.</summary>
	public const int Declaring = 0;

	/// <summary>Everybody else.</summary>
	public const int Defending = 1;

	public static int Other(int side) => side == Declaring ? Defending : Declaring;
}

/// <summary>
/// Something that can be doubled: either the game itself (<see cref="Kind"/> null) or one side's
/// announcement of a bonus. <see cref="Side"/> is the side that owns it.
/// </summary>
public readonly record struct KontraTarget(Bonus? Kind, int Side) {

	public static KontraTarget Game => new(null, Sides.Declaring);

	public bool IsGame => Kind is null;

	public override string ToString() => IsGame
		? "igra"
		: $"{Bonuses.Info(Kind!.Value).SlovenianName} (stran {Side})";
}

/// <summary>A bonus somebody has announced.</summary>
public readonly record struct AnnouncedBonus(Bonus Kind, int Side, int Seat);

/// <summary>
/// The announcement round, held after the talon exchange and before the first card.
///
/// It starts with the declarer and goes round the table; on your turn you either announce something
/// for your side, kontra something the other side owns, or say "dalje". The round ends when it has
/// gone all the way round without anybody doing anything.
///
/// Pagat ultimo may only be announced by whoever holds the pagat, and kralj ultimo only by whoever
/// holds the called king — announcing either one therefore tells the table something real.
/// The kontra ladder is kontra → rekontra → subkontra → mordkontra, alternating sides, ×2 each step
/// up to ×16, and the game and every announcement carry their own separate ladder.
/// </summary>
public sealed class AnnouncementRound {

	/// <summary>Kontra, rekontra, subkontra, mordkontra.</summary>
	public const int MaxKontraLevel = 4;

	private readonly List<AnnouncedBonus> _announcements = [];
	private readonly Card? _calledKing;
	private readonly IReadOnlyList<IReadOnlyList<Card>> _hands;
	private readonly Dictionary<KontraTarget, int> _kontras = [];
	private readonly int[] _sideOfSeat;

	private int _consecutivePasses;

	private AnnouncementRound(
		ContractInfo info,
		IReadOnlyList<int> sideOfSeat,
		int declarerSeat,
		IReadOnlyList<IReadOnlyList<Card>> hands,
		Card? calledKing,
		bool allowKlopKontra) {

		Info = info;
		DeclarerSeat = declarerSeat;
		CurrentSeat = declarerSeat;
		AllowKlopKontra = allowKlopKontra;
		_sideOfSeat = [.. sideOfSeat];
		_hands = hands;
		_calledKing = calledKing;
	}

	public ContractInfo Info { get; }

	public int DeclarerSeat { get; }

	public bool AllowKlopKontra { get; }

	public int CurrentSeat { get; private set; }

	public bool IsComplete { get; private set; }

	public IReadOnlyList<AnnouncedBonus> Announcements => _announcements;

	public static AnnouncementRound Start(
		ContractInfo info,
		IReadOnlyList<int> sideOfSeat,
		int declarerSeat,
		IReadOnlyList<IReadOnlyList<Card>> hands,
		Card? calledKing = null,
		bool allowKlopKontra = false) {

		ArgumentNullException.ThrowIfNull(info);
		ArgumentNullException.ThrowIfNull(sideOfSeat);
		ArgumentNullException.ThrowIfNull(hands);
		ValidateSeat(declarerSeat);

		if (sideOfSeat.Count != TarokConstants.PlayerCount || hands.Count != TarokConstants.PlayerCount) {
			throw new ArgumentException($"Expected {TarokConstants.PlayerCount} seats.", nameof(sideOfSeat));
		}

		return new AnnouncementRound(info, sideOfSeat, declarerSeat, hands, calledKing, allowKlopKontra);
	}

	public int SideOf(int seat) {
		ValidateSeat(seat);
		return _sideOfSeat[seat];
	}

	/// <summary>How many times a target has been doubled: 0 to 4.</summary>
	public int KontraLevel(KontraTarget target) => _kontras.GetValueOrDefault(target);

	/// <summary>1, 2, 4, 8 or 16.</summary>
	public int Multiplier(KontraTarget target) => 1 << KontraLevel(target);

	/// <summary>Whether a bonus has been announced by a side, which is what doubles its value.</summary>
	public bool WasAnnounced(Bonus bonus, int side) =>
		_announcements.Any(entry => entry.Kind == bonus && entry.Side == side);

	/// <summary>What the seat on turn may announce for its own side.</summary>
	public IReadOnlyList<Bonus> LegalAnnouncements() {
		if (IsComplete || !Info.AllowsBonuses) {
			return [];
		}

		int side = _sideOfSeat[CurrentSeat];
		List<Bonus> legal = [];

		foreach (BonusInfo bonus in Bonuses.All) {
			if (WasAnnounced(bonus.Bonus, side)) {
				continue;
			}

			if (bonus.Bonus == Bonus.PagatUltimo && !Holds(CurrentSeat, Card.Pagat)) {
				continue;
			}

			if (bonus.Bonus == Bonus.KingUltimo && (_calledKing is null || !Holds(CurrentSeat, _calledKing.Value))) {
				continue;
			}

			legal.Add(bonus.Bonus);
		}

		return legal;
	}

	/// <summary>What the seat on turn may double.</summary>
	public IReadOnlyList<KontraTarget> LegalKontras() {
		if (IsComplete) {
			return [];
		}

		List<KontraTarget> legal = [];

		if (CanKontraTheGame() && MayRaise(CurrentSeat, KontraTarget.Game)) {
			legal.Add(KontraTarget.Game);
		}

		foreach (AnnouncedBonus announced in _announcements) {
			KontraTarget target = new(announced.Kind, announced.Side);
			if (!legal.Contains(target) && MayRaise(CurrentSeat, target)) {
				legal.Add(target);
			}
		}

		return legal;
	}

	public void Announce(int seat, Bonus bonus) {
		RequireTurn(seat);

		if (!LegalAnnouncements().Contains(bonus)) {
			throw new InvalidOperationException($"Seat {seat} may not announce {Bonuses.Info(bonus).SlovenianName} here.");
		}

		_announcements.Add(new AnnouncedBonus(bonus, _sideOfSeat[seat], seat));
		AfterAction(seat);
	}

	public void Kontra(int seat, KontraTarget target) {
		RequireTurn(seat);

		if (!LegalKontras().Contains(target)) {
			throw new InvalidOperationException($"Seat {seat} may not kontra {target} here.");
		}

		_kontras[target] = KontraLevel(target) + 1;
		AfterAction(seat);
	}

	public void Pass(int seat) {
		RequireTurn(seat);

		_consecutivePasses++;
		if (_consecutivePasses >= TarokConstants.PlayerCount) {
			IsComplete = true;
			return;
		}

		CurrentSeat = (seat + 1) % TarokConstants.PlayerCount;
	}

	private void AfterAction(int seat) {
		// A player may announce or kontra several things in one turn, so the table only moves on
		// when the seat says "dalje"; what an action does is reset the circle of passes.
		_consecutivePasses = 0;
		CurrentSeat = seat;
	}

	private bool CanKontraTheGame() => !Info.IsKlop || AllowKlopKontra;

	/// <summary>
	/// Kontra alternates: the first double comes from the side that does not own the target,
	/// the answer from the owner, and so on up to mordkontra.
	/// </summary>
	private bool MayRaise(int seat, KontraTarget target) {
		int level = KontraLevel(target);
		if (level >= MaxKontraLevel) {
			return false;
		}

		int owner = target.Side;
		int allowed = level % 2 == 0 ? Sides.Other(owner) : owner;

		return _sideOfSeat[seat] == allowed;
	}

	private bool Holds(int seat, Card card) => _hands[seat].Contains(card);

	private void RequireTurn(int seat) {
		ValidateSeat(seat);

		if (IsComplete) {
			throw new InvalidOperationException("The announcements are over.");
		}

		if (seat != CurrentSeat) {
			throw new InvalidOperationException($"It is seat {CurrentSeat}'s turn, not seat {seat}'s.");
		}
	}

	private static void ValidateSeat(int seat) {
		ArgumentOutOfRangeException.ThrowIfNegative(seat);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(seat, TarokConstants.PlayerCount);
	}
}
