using Microsoft.EntityFrameworkCore;
using RF4Catches.Models;

namespace RF4Catches.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Catch> Catches => Set<Catch>();
    public DbSet<FishingSession> FishingSessions => Set<FishingSession>();
    public DbSet<PendingReview> PendingReviews => Set<PendingReview>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Catch>().Property(c => c.SaleValue).HasPrecision(18, 2);
        modelBuilder.Entity<Catch>().Property(c => c.LengthCm).HasPrecision(18, 2);
        modelBuilder.Entity<Catch>().Property(c => c.WeightKg).HasPrecision(18, 4);
        modelBuilder.Entity<FishingSession>().Property(s => s.MarketTotal).HasPrecision(18, 2);
        modelBuilder.Entity<FishingSession>().Property(s => s.HookDepthCm).HasPrecision(18, 2);
        modelBuilder.Entity<FishingSession>().Property(s => s.CafeSilver).HasPrecision(18, 2);
        modelBuilder.Entity<FishingSession>().Property(s => s.MarketSilver).HasPrecision(18, 2);

        // Soft delete: every query against Catch transparently excludes
        // rows flagged IsDeleted. Use .IgnoreQueryFilters() to see them.
        modelBuilder.Entity<Catch>().HasQueryFilter(c => !c.IsDeleted);
    }
}