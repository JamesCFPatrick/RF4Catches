using Microsoft.EntityFrameworkCore;
using RF4Catches.Data;
using RF4Catches.Services;

namespace RF4Catches.Endpoints;

public static class HistoryEndpoints
{
    public static void MapHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/history/content", async (
            IDbContextFactory<AppDbContext> dbContextFactory,
            CancellationToken ct) =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(ct);

            var sessions = await db.FishingSessions
                .Select(s => new HistorySession(
                    s.Id, s.StartedAtUtc, s.EndedAtUtc,
                    new SessionDetails(s.FishingMethod, s.Baits, s.LineClip, s.HookDepthCm, s.MapName, s.MapCoordinates, s.CafeSilver, s.MarketSilver),
                    Array.Empty<HistoryCatch>()))
                .ToListAsync(ct);

            var catches = await db.Catches
                .Select(c => new
                {
                    c.FishingSessionId,
                    Catch = new HistoryCatch(c.Species, c.WeightKg, c.LengthCm, c.Rarity, c.ImagePath, c.CaughtAtUtc)
                })
                .ToListAsync(ct);

            var catchesBySession = catches
                .GroupBy(x => x.FishingSessionId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<HistoryCatch>)g.Select(x => x.Catch).ToList());

            var history = sessions
                .OrderByDescending(s => s.Id)
                .Select(s => s with
                {
                    Catches = catchesBySession.TryGetValue(s.Id, out var list) ? list : Array.Empty<HistoryCatch>()
                })
                .Where(s => s.Catches.Count > 0)
                .ToList();

            return Results.Content(HistoryMarkup.Render(history), "text/html");
        });

        app.MapGet("/history", () => Results.Redirect("/history.html"));
    }
}