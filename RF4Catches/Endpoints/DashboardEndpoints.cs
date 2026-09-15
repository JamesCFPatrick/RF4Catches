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
            await using var db = await dbContextFactory.CreateDbContextAsync(ct);
            var today = DateTimeOffset.UtcNow.Date;

            var activeSessionCatches = sessionService.ActiveSessionId is { } activeSessionId
                ? await db.Catches
                    .Where(c => c.FishingSessionId == activeSessionId)
                    .Select(c => new DashboardCatch(c.Species, c.WeightKg, c.LengthCm, c.Rarity, c.ImagePath, c.CaughtAtUtc, c.FishingSessionId))
                    .ToListAsync(ct)
                : [];

            var activeSessionDetails = sessionService.ActiveSessionId is { } currentSessionId
                ? await db.FishingSessions
                    .Where(s => s.Id == currentSessionId)
                    .Select(s => new SessionDetails(s.FishingMethod, s.Baits, s.LineClip, s.HookDepthCm, s.MapName, s.MapCoordinates, s.CafeSilver, s.MarketSilver))
                    .FirstOrDefaultAsync(ct)
                : null;

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

            return Results.Content(
                DashboardMarkup.RenderContent(new DashboardData(
                    totalCatches, totalWeightKg, averageWeightKg, catchesToday,
                    sessionService.ActiveSessionId, activeSessionDetails, recentCatches)),
                "text/html");
        });

        app.MapGet("/dashboard/session", async (
            IDbContextFactory<AppDbContext> dbContextFactory,
            SessionService sessionService,
            CancellationToken ct) =>
        {
            SessionDetails? details = null;
            if (sessionService.ActiveSessionId is { } activeSessionId)
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(ct);
                details = await db.FishingSessions
                    .Where(s => s.Id == activeSessionId)
                    .Select(s => new SessionDetails(
                        s.FishingMethod, s.Baits, s.LineClip, s.HookDepthCm,
                        s.MapName, s.MapCoordinates, s.CafeSilver, s.MarketSilver))
                    .FirstOrDefaultAsync(ct);
            }

            var data = new DashboardData(0, 0m, 0m, 0, sessionService.ActiveSessionId, details, Array.Empty<DashboardCatch>());
            return Results.Content(DashboardMarkup.RenderSession(data), "text/html");
        });

        app.MapGet("/dashboard", () => Results.Redirect("/"));
    }
}