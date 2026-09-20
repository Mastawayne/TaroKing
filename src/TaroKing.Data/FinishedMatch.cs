using TaroKing.Engine.Announcing;
using TaroKing.Engine.Bidding;
using TaroKing.Engine.Play;
using TaroKing.Engine.Session;

namespace TaroKing.Data;

/// <summary>A seat as a match ended. Everything the store needs, nothing it has to go and look up.</summary>
public sealed record FinishedSeat(
	int Seat,
	string? UserId,
	string Name,
	bool IsBot,
	bool Abandoned,
	int Total,
	int FinalTotal,
	int Radlci);

/// <summary>One hand, distilled for statistics and carrying its log for the replay.</summary>
public sealed record FinishedHand(
	int Number,
	int Seed,
	bool CompulsoryKlop,
	Contract Contract,
	int Declarer,
	bool DeclarerWon,
	int CardPoints,
	int Difference,
	int? PagatUltimoAnnouncedBy,
	int? PagatUltimoWonBy,
	IReadOnlyList<int> Scores,
	IReadOnlyList<GameEvent> Events) {

	/// <summary>Distil a finished hand. The pagat facts come from the tricks, not from any string.</summary>
	public static FinishedHand From(HandState hand, int number) {
		ArgumentNullException.ThrowIfNull(hand);

		if (hand.Phase != GamePhase.Finished || hand.Score is null) {
			throw new InvalidOperationException("The hand is not over yet.");
		}

		int? announcedBy = hand.Events
			.OfType<BonusAnnounced>()
			.Where(announced => announced.Bonus == Bonus.PagatUltimo)
			.Select(announced => (int?)announced.Announcer)
			.FirstOrDefault();

		int? wonBy = null;
		if (hand.Tricks is TrickPlay tricks && tricks.Tricks.Count > 0) {
			Trick last = tricks.Tricks[^1];
			if (last.WinningCard.IsPagat) {
				wonBy = last.WinnerSeat;
			}
		}

		return new FinishedHand(
			Number: number,
			Seed: hand.Seed,
			CompulsoryKlop: hand.IsCompulsoryKlop,
			Contract: hand.PlayedContract!.Value,
			Declarer: hand.Declarer!.Value,
			DeclarerWon: hand.Score.DeclarerWon,
			CardPoints: hand.Score.CardPoints,
			Difference: hand.Score.Difference,
			PagatUltimoAnnouncedBy: announcedBy,
			PagatUltimoWonBy: wonBy,
			Scores: [.. hand.Score.BySeat],
			Events: [.. hand.Events]);
	}

	/// <summary>The same distillation from a stored log, for a hand that only exists as events.</summary>
	public static FinishedHand FromEvents(IReadOnlyList<GameEvent> events, int number) =>
		From(HandState.Replay(events), number);
}

public sealed record FinishedChat(DateTimeOffset At, int Seat, string Who, string Text, bool FromTable);

/// <summary>A whole finished match, ready to be written down.</summary>
public sealed record FinishedMatch(
	MatchKind Kind,
	string SourceId,
	string Name,
	int Rounds,
	DateTimeOffset StartedAt,
	DateTimeOffset FinishedAt,
	IReadOnlyList<FinishedSeat> Seats,
	IReadOnlyList<FinishedHand> Hands,
	IReadOnlyList<FinishedChat> Chat);
