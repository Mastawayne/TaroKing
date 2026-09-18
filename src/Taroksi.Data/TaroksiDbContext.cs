using Microsoft.EntityFrameworkCore;

namespace Taroksi.Data;

/// <summary>
/// Persistence root. Entities are added in Phase 13 (users, matches, hands, events, ratings, chat).
/// </summary>
public sealed class TaroksiDbContext : DbContext {

	public TaroksiDbContext(DbContextOptions<TaroksiDbContext> options) : base(options) {
	}

	protected override void OnModelCreating(ModelBuilder modelBuilder) {
		base.OnModelCreating(modelBuilder);
		modelBuilder.ApplyConfigurationsFromAssembly(typeof(TaroksiDbContext).Assembly);
	}
}
