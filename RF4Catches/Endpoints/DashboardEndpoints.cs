using Microsoft.EntityFrameworkCore;
using RF4Catches.Data;
using RF4Catches.Services;

namespace RF4Catches.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/dashboard/content", async (
            IDbContextFactory<AppDbContext> dbContextFactory,
            SessionService sessionService,
            CancellationToken ct) =>
        {
            var data = await BuildDashboardDataAsync(dbContextFactory, sessionService, ct);
            return Results.Content(DashboardMarkup.RenderContent(data), "text/html");
        });

        app.MapGet("/dashboard/session", async (
            IDbContextFactory<AppDbContext> dbContextFactory,
            SessionService sessionService,
            CancellationToken ct) =>
        {
            var details = await sessionService.GetActiveDetailsAsync(dbContextFactory, ct);
            var data = new DashboardData(0, 0m, 0m, 0, sessionService.ActiveSessionId, details, Array.Empty<DashboardCatch>());
            return Results.Content(DashboardMarkup.RenderSession(data), "text/html");
        });
        
        app.MapGet("/api/history/dashboard", async (
            HistoryDashboardService service,
            CancellationToken ct) =>
        {
            var data = await service.GetDashboardAsync(ct);
            return Results.Ok(data);
        });
        
        app.MapGet("/dashboard", () => Results.Redirect("/"));
        
        app.MapGet("/dashboard/version", async (
            IDbContextFactory<AppDbContext> dbContextFactory,
            SessionService sessionService,
            CancellationToken ct) =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(ct);

            if (sessionService.ActiveSessionId is not { } sessionId)
                return Results.Ok(new { version = "none" });

            var stats = await db.Catches
                .Where(c => c.FishingSessionId == sessionId)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    MaxId = g.Max(c => c.Id),
                    Count = g.Count()
                })
                .SingleOrDefaultAsync(ct);

            var version = stats is null
                ? $"{sessionId}:0:0"
                : $"{sessionId}:{stats.Count}:{stats.MaxId}";

            return Results.Ok(new { version });
        });
    }
    
    
    

    /// <summary>
    /// Builds the aggregate dashboard payload. Shared by the content endpoint
    /// and the delete-catch endpoint (which uses it to refresh stat cards via
    /// an out-of-band swap after a soft delete).
    /// </summary>
    internal static async Task<DashboardData> BuildDashboardDataAsync(
        IDbContextFactory<AppDbContext> dbContextFactory,
        SessionService sessionService,
        CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var today = DateTimeOffset.UtcNow.Date;

        var activeSessionCatches = sessionService.ActiveSessionId is { } activeSessionId
            ? await db.Catches
                .Where(c => c.FishingSessionId == activeSessionId)
                .Select(c => new DashboardCatch(c.Id, c.Species, c.WeightKg, c.LengthCm, c.Rarity, c.ImagePath, c.CaughtAtUtc, c.FishingSessionId))
                .ToListAsync(ct)
            : [];

        // Session details change only on lifecycle events (start, end, save details).
        // Cached in SessionService and invalidated at those points, so this poll
        // doesn't need to hit FishingSessions every 5 seconds.
        var activeSessionDetails = await sessionService.GetActiveDetailsAsync(dbContextFactory, ct);

        activeSessionCatches = activeSessionCatches
            .Select(c => c with
            {
                WeightKg = c.WeightKg is { } w ? CatchWeight.NormalizeKg(w) : null
            })
            .ToList();

        var totalCatches = activeSessionCatches.Count;
        var totalWeightKg = activeSessionCatches.Sum(c => c.WeightKg ?? 0m);
        var averageWeightKg = totalCatches == 0 ? 0m : totalWeightKg / totalCatches;
        var catchesToday = activeSessionCatches.Count(c => c.CaughtAtUtc >= today);
        var recentCatches = activeSessionCatches
            .OrderByDescending(c => c.CaughtAtUtc)
            .Take(25)
            .ToList();

        return new DashboardData(
            totalCatches, totalWeightKg, averageWeightKg, catchesToday,
            sessionService.ActiveSessionId, activeSessionDetails, recentCatches);
    }
}