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

	// --- moderation (Phase 16) ---

	/// <summary>Chat is refused until this moment. Null: not muted.</summary>
	public DateTimeOffset? MutedUntil { get; set; }

	/// <summary>Login and sitting down are refused until this moment; <see cref="DateTimeOffset.MaxValue"/> is permanent. Null: not banned.</summary>
	public DateTimeOffset? BannedUntil { get; set; }

	public string? BanReason { get; set; }

	/// <summary>The last warning a moderator sent, shown once on the profile page.</summary>
	public string? Warning { get; set; }

	/// <summary>The account was deleted on request and anonymised; nothing personal is left on it.</summary>
	public bool IsDeleted { get; set; }

	public List<MatchSeat> Seats { get; set; } = [];

	/// <summary>Whether the lobby should quietly warn people about this player.</summary>
	public bool IsFrequentLeaver => MatchesPlayed >= 4 && MatchesAbandoned * 4 > MatchesPlayed;

	public bool IsMuted(DateTimeOffset now) => MutedUntil is DateTimeOffset until && until > now;

	public bool IsBanned(DateTimeOffset now) => BannedUntil is DateTimeOffset until && until > now;
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

public enum ReportStatus {
	Open = 0,
	Dismissed = 1,
	Actioned = 2
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

	// --- moderation (Phase 16) ---

	public ReportStatus Status { get; set; }

	public string? HandledById { get; set; }

	public DateTimeOffset? HandledAt { get; set; }
}

/// <summary>What a moderator did, to whom, and why. Never deleted.</summary>
public sealed class ModerationAction {

	public int Id { get; set; }

	public string ModeratorId { get; set; } = "";

	public string ModeratorName { get; set; } = "";

	public string TargetUserId { get; set; } = "";

	public string TargetName { get; set; } = "";

	/// <summary>dismiss, warn, mute, unmute, ban, unban.</summary>
	public string Kind { get; set; } = "";

	public string Reason { get; set; } = "";

	/// <summary>When a mute or ban ends; null for actions without a duration.</summary>
	public DateTimeOffset? Until { get; set; }

	public int? ReportId { get; set; }

	public DateTimeOffset At { get; set; }
}

/// <summary>A personal block: the blocked player cannot sit at tables the blocker hosts, and their chat is hidden.</summary>
public sealed class Block {

	public int Id { get; set; }

	public string UserId { get; set; } = "";

	public string BlockedId { get; set; } = "";

	public DateTimeOffset At { get; set; }
}

// --- live tables (Phase 15) ---

/// <summary>
/// An online table as it stands right now, so a restart can put it back. The options, seats and
/// chat are JSON the app writes and reads; the store does not interpret them.
/// </summary>
public sealed class LiveTable {

	/// <summary>The table id the app uses in URLs.</summary>
	public string Id { get; set; } = "";

	public string Name { get; set; } = "";

	public string HostId { get; set; } = "";

	public string OptionsJson { get; set; } = "";

	public string SeatsJson { get; set; } = "";

	public string ChatJson { get; set; } = "";

	/// <summary>0 waiting, 1 playing, 2 finished — the app's TablePhase.</summary>
	public int Phase { get; set; }

	public DateTimeOffset OpenedAt { get; set; }

	public DateTimeOffset? StartedAt { get; set; }

	public DateTimeOffset UpdatedAt { get; set; }

	/// <summary>A finished table that has been written to the match history and can be forgotten.</summary>
	public bool Archived { get; set; }

	public List<LiveEvent> Events { get; set; } = [];
}

/// <summary>One event of one hand of a live table, written the moment it happens.</summary>
public sealed class LiveEvent {

	public long Id { get; set; }

	public string TableId { get; set; } = "";

	public LiveTable Table { get; set; } = null!;

	public int HandNumber { get; set; }

	public int Ordinal { get; set; }

	public string Kind { get; set; } = "";

	public int? Seat { get; set; }

	public string Payload { get; set; } = "";
}
