using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TaroKing.Data;

/// <summary>
/// SQLite has no date type and stores a <see cref="DateTimeOffset"/> as text, which it then refuses
/// to sort. Storing UTC ticks instead sorts correctly, compares correctly, and loses nothing the
/// game cares about — every timestamp here is written in UTC anyway.
/// </summary>
public sealed class UtcTicksConverter() : ValueConverter<DateTimeOffset, long>(
	value => value.UtcTicks,
	ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

/// <summary>
/// Persistence root. Identity owns the user and credential tables; everything else is the
/// game's own record of what was played, by whom, and what it did to the ratings.
/// </summary>
public sealed class TaroKingDbContext(DbContextOptions<TaroKingDbContext> options) : IdentityDbContext<TaroKingUser>(options) {

	protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) {
		base.ConfigureConventions(configurationBuilder);

		// Every timestamp in the model, Identity's LockoutEnd included, goes down as UTC ticks.
		configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcTicksConverter>();
	}

	public DbSet<Match> Matches => Set<Match>();

	public DbSet<MatchSeat> MatchSeats => Set<MatchSeat>();

	public DbSet<Hand> Hands => Set<Hand>();

	public DbSet<HandEvent> Events => Set<HandEvent>();

	public DbSet<RatingChange> Ratings => Set<RatingChange>();

	public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

	public DbSet<BlacklistReport> Reports => Set<BlacklistReport>();

	protected override void OnModelCreating(ModelBuilder modelBuilder) {
		base.OnModelCreating(modelBuilder);

		modelBuilder.Entity<TaroKingUser>(user => {
			user.HasIndex(u => u.Rating);
			user.Ignore(u => u.IsFrequentLeaver);
		});

		modelBuilder.Entity<Match>(match => {
			match.HasIndex(m => m.FinishedAt);
			match.Property(m => m.SourceId).HasMaxLength(32);
			match.Property(m => m.Name).HasMaxLength(64);
		});

		modelBuilder.Entity<MatchSeat>(seat => {
			seat.HasIndex(s => new { s.MatchId, s.Seat }).IsUnique();
			seat.HasIndex(s => s.UserId);
			seat.Property(s => s.Name).HasMaxLength(64);
			seat.Ignore(s => s.RatingDelta);

			seat.HasOne(s => s.Match)
				.WithMany(m => m.Seats)
				.HasForeignKey(s => s.MatchId)
				.OnDelete(DeleteBehavior.Cascade);

			seat.HasOne(s => s.User)
				.WithMany(u => u.Seats)
				.HasForeignKey(s => s.UserId)
				.OnDelete(DeleteBehavior.SetNull);
		});

		modelBuilder.Entity<Hand>(hand => {
			hand.HasIndex(h => new { h.MatchId, h.Number }).IsUnique();

			hand.HasOne(h => h.Match)
				.WithMany(m => m.Hands)
				.HasForeignKey(h => h.MatchId)
				.OnDelete(DeleteBehavior.Cascade);
		});

		modelBuilder.Entity<HandEvent>(line => {
			line.HasIndex(e => new { e.HandId, e.Ordinal }).IsUnique();
			line.Property(e => e.Kind).HasMaxLength(32);
			line.Property(e => e.Payload).HasMaxLength(256);

			line.HasOne(e => e.Hand)
				.WithMany(h => h.Events)
				.HasForeignKey(e => e.HandId)
				.OnDelete(DeleteBehavior.Cascade);
		});

		modelBuilder.Entity<RatingChange>(change => {
			change.HasIndex(c => new { c.UserId, c.At });
			change.Ignore(c => c.Delta);

			change.HasOne(c => c.User)
				.WithMany()
				.HasForeignKey(c => c.UserId)
				.OnDelete(DeleteBehavior.Cascade);

			change.HasOne(c => c.Match)
				.WithMany()
				.HasForeignKey(c => c.MatchId)
				.OnDelete(DeleteBehavior.Cascade);
		});

		modelBuilder.Entity<ChatMessage>(message => {
			message.HasIndex(m => new { m.MatchId, m.At });
			message.Property(m => m.Who).HasMaxLength(64);
			message.Property(m => m.Text).HasMaxLength(256);

			message.HasOne(m => m.Match)
				.WithMany(m => m.Chat)
				.HasForeignKey(m => m.MatchId)
				.OnDelete(DeleteBehavior.Cascade);
		});

		modelBuilder.Entity<BlacklistReport>(report => {
			report.HasIndex(r => new { r.ReporterId, r.ReportedId }).IsUnique();
			report.HasIndex(r => r.ReportedId);
			report.Property(r => r.Reason).HasMaxLength(200);

			report.HasOne(r => r.Reporter)
				.WithMany()
				.HasForeignKey(r => r.ReporterId)
				.OnDelete(DeleteBehavior.Cascade);

			report.HasOne(r => r.Reported)
				.WithMany()
				.HasForeignKey(r => r.ReportedId)
				.OnDelete(DeleteBehavior.Cascade);
		});
	}
}
