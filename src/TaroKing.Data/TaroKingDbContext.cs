using Microsoft.EntityFrameworkCore;

namespace TaroKing.Data;

/// <summary>
/// Persistence root. Entities are added in Phase 13 (users, matches, hands, events, ratings, chat).
/// </summary>
public sealed class TaroKingDbContext : DbContext {

	public TaroKingDbContext(DbContextOptions<TaroKingDbContext> options) : base(options) {
	}

	protected override void OnModelCreating(ModelBuilder modelBuilder) {
		base.OnModelCreating(modelBuilder);
		modelBuilder.ApplyConfigurationsFromAssembly(typeof(TaroKingDbContext).Assembly);
	}
}
