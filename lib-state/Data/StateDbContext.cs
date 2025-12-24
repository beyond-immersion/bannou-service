#nullable enable

using Microsoft.EntityFrameworkCore;

namespace BeyondImmersion.BannouService.State.Data;

/// <summary>
/// EF Core DbContext for MySQL state storage.
/// </summary>
public class StateDbContext : DbContext
{
    /// <summary>
    /// State entries table.
    /// </summary>
    public DbSet<StateEntry> StateEntries { get; set; } = default!;

    /// <summary>
    /// Creates a new StateDbContext with the specified options.
    /// </summary>
    public StateDbContext(DbContextOptions<StateDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Configures the entity model.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure composite primary key (StoreName + Key)
        modelBuilder.Entity<StateEntry>()
            .HasKey(e => new { e.StoreName, e.Key });

        // Configure indexes
        modelBuilder.Entity<StateEntry>()
            .HasIndex(e => e.StoreName)
            .HasDatabaseName("IX_state_entries_store_name");

        modelBuilder.Entity<StateEntry>()
            .HasIndex(e => e.UpdatedAt)
            .HasDatabaseName("IX_state_entries_updated_at");

        // Configure version as concurrency token
        modelBuilder.Entity<StateEntry>()
            .Property(e => e.Version)
            .IsConcurrencyToken();
    }
}
