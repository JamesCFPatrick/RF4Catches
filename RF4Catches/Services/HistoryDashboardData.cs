namespace RF4Catches.Services;

/// <summary>
/// One point on a time-series chart. It is UTC and gets converted to local
/// time at render, matching how the markup helpers handle CaughtAtUtc.
/// </summary>
public sealed record HistoryPoint(int SessionId, DateTimeOffset At, double Value);

/// <summary>A single species/category with a count.</summary>
public sealed record HistoryBreakdown(string Label, int Count, decimal TotalWeightKg);

/// <summary>
/// Aggregate "at a glance" numbers. All silver is a decimal (rounding-safe);
/// ratio metrics are doubles because they're charted, not displayed raw.
/// </summary>
public sealed record HistorySummary(
    int TotalSessions,
    int SessionsWithSilver,
    int SessionsExcludedFromSilver,
    int TotalCatches,
    decimal TotalSilver,
    TimeSpan TotalFishingTime,
    double AverageSilverPerHour,
    double BestSilverPerHour,
    int? BestSilverPerHourSessionId,
    string? MostCaughtSpecies,
    int MostCaughtSpeciesCount);

public sealed record HistoryDashboardData(
    HistorySummary Summary,
    IReadOnlyList<HistoryPoint> SilverPerHourBySession,
    IReadOnlyList<HistoryPoint> CumulativeSilverBySession,
    IReadOnlyList<HistoryPoint> CatchesPerSession,
    IReadOnlyList<HistoryBreakdown> TopSpecies,
    IReadOnlyList<HistoryBreakdown> RarityDistribution,
    IReadOnlyList<HistoryBreakdown> ByMap);