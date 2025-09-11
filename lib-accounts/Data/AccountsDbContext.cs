using Microsoft.EntityFrameworkCore;

namespace BeyondImmersion.BannouService.Accounts.Data;

/// <summary>
/// Entity Framework database context for accounts service.
/// </summary>
public class AccountsDbContext : DbContext
{
    public AccountsDbContext(DbContextOptions<AccountsDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Accounts table.
    /// </summary>
    public DbSet<AccountEntity> Accounts { get; set; } = null!;

    /// <summary>
    /// Authentication methods table.
    /// </summary>
    public DbSet<AuthMethodEntity> AuthMethods { get; set; } = null!;

    /// <summary>
    /// Account roles table.
    /// </summary>
    public DbSet<AccountRoleEntity> AccountRoles { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Account entity
        modelBuilder.Entity<AccountEntity>(entity =>
        {
            entity.HasKey(e => e.AccountId);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.DeletedAt);
            entity.HasQueryFilter(e => e.DeletedAt == null); // Global soft delete filter

            // Configure relationships
            entity.HasMany(e => e.AuthMethods)
                .WithOne(am => am.Account)
                .HasForeignKey(am => am.AccountId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.AccountRoles)
                .WithOne(ar => ar.Account)
                .HasForeignKey(ar => ar.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure AuthMethod entity
        modelBuilder.Entity<AuthMethodEntity>(entity =>
        {
            entity.HasKey(e => e.AuthMethodId);
            entity.HasIndex(e => new { e.Provider, e.ProviderUserId }).IsUnique();
            entity.HasIndex(e => e.AccountId);
        });

        // Configure AccountRole entity
        modelBuilder.Entity<AccountRoleEntity>(entity =>
        {
            entity.HasKey(e => new { e.AccountId, e.Role }); // Composite key
            entity.HasIndex(e => e.AccountId);
            entity.HasIndex(e => e.Role);
        });
    }

    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return await base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Automatically update timestamps on entity changes.
    /// </summary>
    private void UpdateTimestamps()
    {
        var entries = ChangeTracker.Entries<AccountEntity>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = DateTime.UtcNow;
            }
            entry.Entity.UpdatedAt = DateTime.UtcNow;
        }
    }
}