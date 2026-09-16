using Microsoft.EntityFrameworkCore;
using RF4Catches.Data;

namespace RF4Catches.Services;

public sealed class HistoryDashboardService(
    IDbContextFactory<AppDbContext> dbContextFactory,
    ILogger<HistoryDashboardService> logger)
{
    private static readonly TimeSpan MinimumDurationForRate = TimeSpan.FromMinutes(3);

    public async Task<HistoryDashboardData> GetDashboardAsync(CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        // The global query filter on FishingSession already excludes soft-deleted
        // sessions, so no explicit IsDeleted check is needed here.
        var sessions = await db.FishingSessions
            .Where(s => s.EndedAtUtc != null)
            .Select(s => new SessionRow(
                s.Id,
                s.StartedAtUtc,
                s.EndedAtUtc!.Value,
                (s.CafeSilver ?? 0m) + (s.MarketSilver ?? 0m),
                s.CafeSilver != null || s.MarketSilver != null,
                s.MapName))
            .ToListAsync(ct);

        // Order in-memory — SQLite can't ORDER BY a DateTimeOffset.
        sessions = sessions
            .OrderBy(s => s.StartedAtUtc)
            .ToList();

        // Catches don't have a query filter that reaches through to the parent
        // session, so we filter explicitly to exclude orphaned-from-soft-delete.
        var catches = await db.Catches
            .Where(c => c.FishingSession != null &&
                        !c.FishingSession.IsDeleted &&
                        c.FishingSession.EndedAtUtc != null)
            .Select(c => new CatchRow(
                c.Id,
                c.Species,
                c.WeightKg,
                c.Rarity,
                c.FishingSessionId))
            .ToListAsync(ct);

        var summary = BuildSummary(sessions, catches);
        var silverPerHour = BuildSilverPerHourSeries(sessions);
        var cumulativeSilver = BuildCumulativeSilverSeries(sessions);
        var catchesPerSession = BuildCatchesPerSessionSeries(sessions, catches);
        var topSpecies = BuildTopSpecies(catches);
        var rarityDistribution = BuildRarityDistribution(catches);
        var byMap = BuildByMap(sessions);

        logger.LogInformation(
            "History dashboard computed: {Sessions} sessions, {Catches} catches, {Silver} total silver.",
            summary.TotalSessions, summary.TotalCatches, summary.TotalSilver);

        return new HistoryDashboardData(
            summary,
            silverPerHour,
            cumulativeSilver,
            catchesPerSession,
            topSpecies,
            rarityDistribution,
            byMap);
    }

    private static HistorySummary BuildSummary(
        IReadOnlyList<SessionRow> sessions,
        IReadOnlyList<CatchRow> catches)
    {
        var sessionsWithSilver = sessions.Where(s => s.HasSilver).ToList();
        var totalSilver = sessionsWithSilver.Sum(s => s.Silver);
        var totalFishingTime = sessions.Aggregate(
            TimeSpan.Zero,
            (acc, s) => acc + (s.EndedAtUtc - s.StartedAtUtc));

        var rateSessions = sessionsWithSilver
            .Select(s => new
            {
                Session = s,
                DurationHours = (s.EndedAtUtc - s.StartedAtUtc).TotalHours
            })
            .Where(x => x.DurationHours >= MinimumDurationForRate.TotalHours)
            .ToList();

        var averageSilverPerHour = rateSessions.Count == 0
            ? 0d
            : rateSessions.Average(x => (double)x.Session.Silver / x.DurationHours);

        var best = rateSessions
            .OrderByDescending(x => (double)x.Session.Silver / x.DurationHours)
            .FirstOrDefault();

        var speciesGroup = catches
            .GroupBy(c => TextCleanup.CleanFishName(c.Species))
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        return new HistorySummary(
            TotalSessions: sessions.Count,
            SessionsWithSilver: sessionsWithSilver.Count,
            SessionsExcludedFromSilver: sessions.Count - sessionsWithSilver.Count,
            TotalCatches: catches.Count,
            TotalSilver: totalSilver,
            TotalFishingTime: totalFishingTime,
            AverageSilverPerHour: averageSilverPerHour,
            BestSilverPerHour: best is null ? 0d : (double)best.Session.Silver / best.DurationHours,
            BestSilverPerHourSessionId: best?.Session.Id,
            MostCaughtSpecies: speciesGroup?.Key,
            MostCaughtSpeciesCount: speciesGroup?.Count() ?? 0);
    }

    private static IReadOnlyList<HistoryPoint> BuildSilverPerHourSeries(
        IReadOnlyList<SessionRow> sessions)
    {
        return sessions
            .Where(s => s.HasSilver)
            .Select(s => new
            {
                Session = s,
                Duration = s.EndedAtUtc - s.StartedAtUtc
            })
            .Where(x => x.Duration >= MinimumDurationForRate)
            .Select(x => new HistoryPoint(
                x.Session.Id,
                x.Session.StartedAtUtc,
                (double)x.Session.Silver / x.Duration.TotalHours))
            .ToList();
    }

    private static IReadOnlyList<HistoryPoint> BuildCumulativeSilverSeries(
        IReadOnlyList<SessionRow> sessions)
    {
        var points = new List<HistoryPoint>(sessions.Count);
        var running = 0m;
        foreach (var session in sessions.Where(s => s.HasSilver))
        {
            running += session.Silver;
            points.Add(new HistoryPoint(session.Id, session.StartedAtUtc, (double)running));
        }
        return points;
    }

    private static IReadOnlyList<HistoryPoint> BuildCatchesPerSessionSeries(
        IReadOnlyList<SessionRow> sessions,
        IReadOnlyList<CatchRow> catches)
    {
        var bySession = catches
            .GroupBy(c => c.FishingSessionId)
            .ToDictionary(g => g.Key, g => g.Count());

        return sessions
            .Select(s => new HistoryPoint(
                s.Id,
                s.StartedAtUtc,
                bySession.TryGetValue(s.Id, out var count) ? count : 0))
            .ToList();
    }

    private static IReadOnlyList<HistoryBreakdown> BuildTopSpecies(
        IReadOnlyList<CatchRow> catches)
    {
        return catches
            .GroupBy(c => TextCleanup.CleanFishName(c.Species))
            .Select(g => new HistoryBreakdown(
                g.Key,
                g.Count(),
                g.Sum(c => c.WeightKg is { } w ? CatchWeight.NormalizeKg(w) : 0m)))
            .OrderByDescending(b => b.Count)
            .Take(10)
            .ToList();
    }

    private static IReadOnlyList<HistoryBreakdown> BuildRarityDistribution(
        IReadOnlyList<CatchRow> catches)
    {
        return catches
            .SelectMany(c => SplitRarityTags(c.Rarity))
            .GroupBy(tag => tag)
            .Select(g => new HistoryBreakdown(g.Key, g.Count(), 0m))
            .OrderByDescending(b => b.Count)
            .ToList();
    }

    private static IReadOnlyList<HistoryBreakdown> BuildByMap(
        IReadOnlyList<SessionRow> sessions)
    {
        return sessions
            .Where(s => !string.IsNullOrWhiteSpace(s.MapName))
            .GroupBy(s => s.MapName!.Trim())
            .Select(g => new HistoryBreakdown(
                g.Key,
                g.Count(),
                g.Sum(s => s.Silver)))
            .OrderByDescending(b => b.TotalWeightKg)
            .ToList();
    }

    private static IEnumerable<string> SplitRarityTags(string? rarity)
    {
        if (string.IsNullOrWhiteSpace(rarity)) yield break;
        foreach (var tag in rarity.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return tag;
    }

    private sealed record SessionRow(
        int Id,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset EndedAtUtc,
        decimal Silver,
        bool HasSilver,
        string? MapName);

    private sealed record CatchRow(
        int Id,
        string Species,
        decimal? WeightKg,
        string? Rarity,
        int FishingSessionId);
}