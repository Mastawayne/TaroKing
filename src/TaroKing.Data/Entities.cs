using Microsoft.AspNetCore.Identity;

using TaroKing.Engine.Bidding;

namespace TaroKing.Data;

/// <summary>
/// A registered player. Identity keeps the credentials; the rest is what the lobby and the
/// leaderboard want to know about them.
/// </summary>
public sealed class TaroKingUser : IdentityUser {

	public const int StartingRating = 1000;

	public int Rating { get; set; } = StartingRating;

	/// <summary>Matches that moved the rating — bots and guests never do.</summary>
	public int RatedMatches { get; set; }

	public int MatchesPlayed { get; set; }

	/// <summary>Matches a bot had to finish for them, because they stood up or never came back.</summary>
	public int MatchesAbandoned { get; set; }

	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

	public List<MatchSeat> Seats { get; set; } = [];

	/// <summary>Whether the lobby should quietly warn people about this player.</summary>
	public bool IsFrequentLeaver => MatchesPlayed >= 4 && MatchesAbandoned * 4 > MatchesPlayed;
}

public enum MatchKind {
	/// <summary>One person against three bots.</summary>
	Local = 0,

	/// <summary>An online table.</summary>
	Online = 1
}

/// <summary>A finished session: a run of hands on one sheet.</summary>
public sealed class Match {

	public int Id { get; set; }

	public MatchKind Kind { get; set; }

	/// <summary>The table or session id it was played under, for tracing back to the log.</summary>
	public string SourceId { get; set; } = "";

	public string Name { get; set; } = "";

	public int Rounds { get; set; }

	/// <summary>Whether the rating moved. Needs at least two registered humans at the table.</summary>
	public bool Rated { get; set; }

	public DateTimeOffset StartedAt { get; set; }

	public DateTimeOffset FinishedAt { get; set; }

	public List<MatchSeat> Seats { get; set; } = [];

	public List<Hand> Hands { get; set; } = [];

	public List<ChatMessage> Chat { get; set; } = [];
}

/// <summary>Who sat where, and what it did to them.</summary>
public sealed class MatchSeat {

	public int Id { get; set; }

	public int MatchId { get; set; }

	public Match Match { get; set; } = null!;

	public int Seat { get; set; }

	/// <summary>Null for a bot or a guest.</summary>
	public string? UserId { get; set; }

	public TaroKingUser? User { get; set; }

	/// <summary>The name shown at the table — a guest's name tag, a bot's label, a user's name.</summary>
	public string Name { get; set; } = "";

	public bool IsBot { get; set; }

	/// <summary>A bot finished the match in this chair.</summary>
	public bool Abandoned { get; set; }

	/// <summary>The sheet total before the radlci were settled.</summary>
	public int Total { get; set; }

	/// <summary>The sheet total with the radlci charged.</summary>
	public int FinalTotal { get; set; }

	public int Radlci { get; set; }

	public int? RatingBefore { get; set; }

	public int? RatingAfter { get; set; }

	public int RatingDelta => (RatingAfter ?? 0) - (RatingBefore ?? 0);
}

/// <summary>One hand, with enough on it for statistics without replaying, and the log for when you do.</summary>
public sealed class Hand {

	public int Id { get; set; }

	public int MatchId { get; set; }

	public Match Match { get; set; } = null!;

	public int Number { get; set; }

	public int Seed { get; set; }

	public bool CompulsoryKlop { get; set; }

	public Contract Contract { get; set; }

	public int Declarer { get; set; }

	public bool DeclarerWon { get; set; }

	/// <summary>Card points the declaring side took. Zero in klop.</summary>
	public int CardPoints { get; set; }

	/// <summary>The rounded margin over 35. Zero where the contract does not score one.</summary>
	public int Difference { get; set; }

	/// <summary>Who announced pagat ultimo, if anybody did.</summary>
	public int? PagatUltimoAnnouncedBy { get; set; }

	/// <summary>Who actually took the last trick with the pagat, if anybody did.</summary>
	public int? PagatUltimoWonBy { get; set; }

	public int Score0 { get; set; }

	public int Score1 { get; set; }

	public int Score2 { get; set; }

	public int Score3 { get; set; }

	public List<HandEvent> Events { get; set; } = [];

	public int ScoreFor(int seat) => seat switch {
		0 => Score0,
		1 => Score1,
		2 => Score2,
		3 => Score3,
		_ => throw new ArgumentOutOfRangeException(nameof(seat), seat, "Four seats.")
	};

	public void SetScores(IReadOnlyList<int> scores) {
		ArgumentNullException.ThrowIfNull(scores);

		Score0 = scores[0];
		Score1 = scores[1];
		Score2 = scores[2];
		Score3 = scores[3];
	}
}

/// <summary>One line of a hand's log. The log is the hand: replaying it rebuilds the exact state.</summary>
public sealed class HandEvent {

	public int Id { get; set; }

	public int HandId { get; set; }

	public Hand Hand { get; set; } = null!;

	public int Ordinal { get; set; }

	/// <summary>The event's kind, as <see cref="EventCodec"/> names it.</summary>
	public string Kind { get; set; } = "";

	/// <summary>The seat that acted, or null for something the table did.</summary>
	public int? Seat { get; set; }

	/// <summary>The event's data, as <see cref="EventCodec"/> writes it.</summary>
	public string Payload { get; set; } = "";
}

/// <summary>The rating book: one line per rated seat per match.</summary>
public sealed class RatingChange {

	public int Id { get; set; }

	public string UserId { get; set; } = "";

	public TaroKingUser User { get; set; } = null!;

	public int MatchId { get; set; }

	public Match Match { get; set; } = null!;

	public int Before { get; set; }

	public int After { get; set; }

	public int Delta => After - Before;

	public DateTimeOffset At { get; set; }
}

public sealed class ChatMessage {

	public int Id { get; set; }

	public int MatchId { get; set; }

	public Match Match { get; set; } = null!;

	public DateTimeOffset At { get; set; }

	/// <summary>-1 for the table itself.</summary>
	public int Seat { get; set; }

	public string Who { get; set; } = "";

	public string Text { get; set; } = "";

	public bool FromTable { get; set; }
}

/// <summary>Somebody asked for somebody else to be kept off their tables.</summary>
public sealed class BlacklistReport {

	public int Id { get; set; }

	public string ReporterId { get; set; } = "";

	public TaroKingUser Reporter { get; set; } = null!;

	public string ReportedId { get; set; } = "";

	public TaroKingUser Reported { get; set; } = null!;

	public string Reason { get; set; } = "";

	public DateTimeOffset At { get; set; }
}
