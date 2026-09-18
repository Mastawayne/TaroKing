using TaroKing.Engine.Cards;

namespace TaroKing.Engine.Dealing;

/// <summary>
/// The called king ("klic kralja"). In three, two and one the declarer names a suit before touching
/// the talon; whoever holds that king is the declarer's partner, and neither of them says so —
/// the partnership only becomes public when the king falls.
///
/// Calling a king you hold yourself is allowed and simply means you play alone. So does calling one
/// that turns out to be lying in the talon.
/// </summary>
public sealed class KingCall {

	private KingCall(Suit suit, int declarerSeat, int? partnerSeat, bool kingIsInTalon, bool declarerHoldsCalledKing) {
		Suit = suit;
		DeclarerSeat = declarerSeat;
		PartnerSeat = partnerSeat;
		KingIsInTalon = kingIsInTalon;
		DeclarerHoldsCalledKing = declarerHoldsCalledKing;
	}

	public Suit Suit { get; }

	public Card King => Card.Of(Suit, SuitRank.King);

	public int DeclarerSeat { get; }

	/// <summary>The seat holding the called king, or null when nobody else does.</summary>
	public int? PartnerSeat { get; }

	public bool KingIsInTalon { get; }

	public bool DeclarerHoldsCalledKing { get; }

	/// <summary>True when the declarer ends up against all three opponents.</summary>
	public bool DeclarerIsAlone => PartnerSeat is null;

	/// <summary>Work out who, if anyone, the declarer just called to their side.</summary>
	public static KingCall Resolve(
		Suit suit,
		int declarerSeat,
		IReadOnlyList<IReadOnlyList<Card>> hands,
		IReadOnlyList<Card> talon) {

		ArgumentNullException.ThrowIfNull(hands);
		ArgumentNullException.ThrowIfNull(talon);

		if (suit == Suit.Trump || !Enum.IsDefined(suit)) {
			throw new ArgumentOutOfRangeException(nameof(suit), suit, "A king is called in a plain suit.");
		}

		if (hands.Count != TarokConstants.PlayerCount) {
			throw new ArgumentException($"Expected {TarokConstants.PlayerCount} hands.", nameof(hands));
		}

		ArgumentOutOfRangeException.ThrowIfNegative(declarerSeat);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(declarerSeat, TarokConstants.PlayerCount);

		Card king = Card.Of(suit, SuitRank.King);

		if (talon.Contains(king)) {
			return new KingCall(suit, declarerSeat, partnerSeat: null, kingIsInTalon: true, declarerHoldsCalledKing: false);
		}

		for (int seat = 0; seat < hands.Count; seat++) {
			if (!hands[seat].Contains(king)) {
				continue;
			}

			return seat == declarerSeat
				? new KingCall(suit, declarerSeat, partnerSeat: null, kingIsInTalon: false, declarerHoldsCalledKing: true)
				: new KingCall(suit, declarerSeat, partnerSeat: seat, kingIsInTalon: false, declarerHoldsCalledKing: false);
		}

		throw new ArgumentException($"The {king} is in neither a hand nor the talon.", nameof(hands));
	}
}
