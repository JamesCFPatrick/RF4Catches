namespace RF4Catches.Data;

public class AppDbContext(Microsoft.EntityFrameworkCore.DbContextOptions<AppDbContext> options) : Microsoft.EntityFrameworkCore.DbContext(options)
{
    public Microsoft.EntityFrameworkCore.DbSet<Models.Catch> Catches => Set<Models.Catch>();
    public Microsoft.EntityFrameworkCore.DbSet<Models.FishingSession> FishingSessions => Set<Models.FishingSession>();
    public Microsoft.EntityFrameworkCore.DbSet<Models.PendingReview> PendingReviews => Set<Models.PendingReview>();

    protected override void OnModelCreating(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Models.Catch>().Property(c => c.SaleValue).HasPrecision(18, 2);
        modelBuilder.Entity<Models.Catch>().Property(c => c.LengthCm).HasPrecision(18, 2);
        modelBuilder.Entity<Models.FishingSession>().Property(s => s.MarketTotal).HasPrecision(18, 2);
        modelBuilder.Entity<Models.FishingSession>().Property(s => s.HookDepthCm).HasPrecision(18, 2);
        modelBuilder.Entity<Models.FishingSession>().Property(s => s.CafeSilver).HasPrecision(18, 2);
        modelBuilder.Entity<Models.FishingSession>().Property(s => s.MarketSilver).HasPrecision(18, 2);
    }
}
