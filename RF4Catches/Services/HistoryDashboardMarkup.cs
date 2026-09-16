using System.Globalization;
using System.Net;
using System.Text;

namespace RF4Catches.Services;

public static class HistoryDashboardMarkup
{
    private const int ChartWidth = 720;
    private const int ChartHeight = 220;

    public static string RenderPage(HistoryDashboardData data)
{
    if (data.Summary.TotalSessions == 0)
        return """
            <div class="rounded-box border border-base-300 bg-base-100 p-10 text-center text-base-content/60">
              No sessions recorded yet. Catch some fish and come back.
            </div>
            """;

    var sb = new StringBuilder();
    sb.Append(RenderStatCards(data.Summary));

    sb.Append(RenderLineChartCard(
        "Silver per hour",
        data.Summary.SessionsExcludedFromSilver > 0
            ? $"{data.Summary.SessionsExcludedFromSilver} session(s) excluded (no silver recorded)"
            : "Earnings rate for each completed session",
        data.SilverPerHourBySession,
        "#22d3ee",
        v => $"{v:N0}",
        "No silver recorded yet. Fill in the session details form after a session ends to see this chart."));

    sb.Append("""<div class="mt-6 grid gap-4 lg:grid-cols-2">""");
    sb.Append(RenderLineChartCard(
        "Cumulative silver",
        "Running total across all completed sessions",
        data.CumulativeSilverBySession,
        "#8b5cf6",
        v => $"{v:N0}",
        "No silver recorded yet.",
        compact: true));
    sb.Append(RenderBarChartCard(
        "Catches per session",
        "Number of fish caught in each completed session",
        data.CatchesPerSession,
        "#22d3ee"));
    sb.Append("</div>");

    sb.Append("""<div class="mt-6 grid gap-4 lg:grid-cols-3">""");
    sb.Append(RenderHorizontalBarsCard(
        "Top species",
        "Most caught, top 10",
        data.TopSpecies,
        _ => "#22d3ee"));
    sb.Append(RenderHorizontalBarsCard(
        "Rarity distribution",
        "How often each tag appears",
        data.RarityDistribution,
        b => $"#{GetRarityColor(b.Label)}"));
    sb.Append(RenderHorizontalBarsCard(
        "By map",
        "Silver earned per location",
        data.ByMap,
        _ => "#22d3ee",
        showWeight: true,
        weightLabel: "silver"));
    sb.Append("</div>");

    return sb.ToString();
}

    private static string RenderStatCards(HistorySummary summary)
    {
        var totalTime = FormatDuration(summary.TotalFishingTime);
        var avgSilverPerHour = summary.AverageSilverPerHour > 0
            ? $"{summary.AverageSilverPerHour:N0}"
            : "—";
        var bestSilverPerHour = summary.BestSilverPerHour > 0
            ? $"{summary.BestSilverPerHour:N0}"
            : "—";
        var bestSession = summary.BestSilverPerHourSessionId is { } id
            ? $"Session #{id}"
            : "No data yet";
        var topSpeciesName = summary.MostCaughtSpecies is { Length: > 0 } species
            ? WebUtility.HtmlEncode(TextCleanup.CleanFishName(species))
            : "—";
        var topSpeciesDesc = summary.MostCaughtSpecies is { Length: > 0 }
            ? $"{summary.MostCaughtSpeciesCount} caught"
            : "Most caught fish";

        return $"""
                <div class="mb-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-6">
                  {StatCard("Total silver", $"{summary.TotalSilver:N0}", $"{summary.SessionsWithSilver} sessions with earnings")}
                  {StatCard("Avg silver/h", avgSilverPerHour, "Across silver-recorded sessions")}
                  {StatCard("Best silver/h", bestSilverPerHour, bestSession)}
                  {StatCard("Total time", totalTime, $"{summary.TotalSessions} sessions fished")}
                  {StatCard("Total catches", summary.TotalCatches.ToString("N0", CultureInfo.InvariantCulture), $"{summary.SessionsExcludedFromSilver} sessions without silver")}
                  {StatCard("Top species", topSpeciesName, topSpeciesDesc)}
                </div>
                """;
    }

    private static string StatCard(string title, string value, string description) => $"""
         <div class="stat min-w-0 rounded-box border border-base-300 bg-base-100 p-5 shadow-xl">
           <div class="stat-title text-xs mb-1">{WebUtility.HtmlEncode(title)}</div>
           <div class="stat-value text-primary text-xl leading-tight break-words py-1">{value}</div>
           <div class="stat-desc text-xs whitespace-normal mt-1">{WebUtility.HtmlEncode(description)}</div>
         </div>
         """;

    private static string RenderLineChartCard(
        string title,
        string subtitle,
        IReadOnlyList<HistoryPoint> points,
        string color,
        Func<double, string> formatValue,
        string emptyMessage,
        bool compact = false)
    {
        var body = points.Count == 0
            ? $"""<p class="py-10 text-center text-sm text-base-content/50">{WebUtility.HtmlEncode(emptyMessage)}</p>"""
            : RenderLineChart(points, color, formatValue, compact ? 160 : ChartHeight);

        return RenderCard(title, subtitle, body);
    }

    private static string RenderLineChart(
        IReadOnlyList<HistoryPoint> points,
        string color,
        Func<double, string> formatValue,
        int height)
    {
        const int padLeft = 60;
        const int padRight = 20;
        const int padTop = 16;
        const int padBottom = 32;
        var width = ChartWidth;
        var innerW = width - padLeft - padRight;
        var innerH = height - padTop - padBottom;

        var minY = 0d;
        var maxY = points.Max(p => p.Value);
        if (maxY <= 0) maxY = 1;
        maxY *= 1.1;

        var count = points.Count;
        string X(int i) => count == 1
            ? (padLeft + innerW / 2).ToString("F1", CultureInfo.InvariantCulture)
            : (padLeft + i * innerW / (double)(count - 1)).ToString("F1", CultureInfo.InvariantCulture);
        string Y(double v) => (padTop + innerH - (v - minY) / (maxY - minY) * innerH)
            .ToString("F1", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.Append($"""<svg viewBox="0 0 {width} {height}" class="w-full h-auto">""");

        for (var i = 0; i <= 4; i++)
        {
            var value = minY + (maxY - minY) * i / 4.0;
            var y = Y(value);
            sb.Append($"""<line x1="{padLeft}" y1="{y}" x2="{width - padRight}" y2="{y}" stroke="rgba(148,163,184,0.12)" stroke-width="1" />""");
            sb.Append($"""<text x="{padLeft - 8}" y="{y}" text-anchor="end" dominant-baseline="middle" fill="rgba(148,163,184,0.7)" font-size="11">{WebUtility.HtmlEncode(formatValue(value))}</text>""");
        }

        var labelStep = Math.Max(1, count / 8);
        for (var i = 0; i < count; i += labelStep)
        {
            var x = X(i);
            var label = points[i].At.ToLocalTime().ToString("dd MMM", CultureInfo.InvariantCulture);
            sb.Append($"""<text x="{x}" y="{height - 8}" text-anchor="middle" fill="rgba(148,163,184,0.7)" font-size="11">{WebUtility.HtmlEncode(label)}</text>""");
        }

        var path = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            path.Append(i == 0 ? 'M' : 'L');
            path.Append(X(i)).Append(' ').Append(Y(points[i].Value));
            if (i < count - 1) path.Append(' ');
        }

        var areaPath = new StringBuilder(path.ToString());
        areaPath.Append(' ').Append('L').Append(X(count - 1)).Append(' ').Append(Y(minY));
        areaPath.Append(' ').Append('L').Append(X(0)).Append(' ').Append(Y(minY));
        areaPath.Append(" Z");
        sb.Append($"""<path d="{areaPath}" fill="{color}" fill-opacity="0.08" />""");
        sb.Append($"""<path d="{path}" fill="none" stroke="{color}" stroke-width="2" stroke-linejoin="round" stroke-linecap="round" />""");

        for (var i = 0; i < count; i++)
        {
            var x = X(i);
            var y = Y(points[i].Value);
            var tooltip = $"Session #{points[i].SessionId} · {points[i].At.ToLocalTime():dd MMM HH:mm} · {formatValue(points[i].Value)}";
            sb.Append($"""<circle cx="{x}" cy="{y}" r="3" fill="{color}"><title>{WebUtility.HtmlEncode(tooltip)}</title></circle>""");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string RenderBarChartCard(
        string title,
        string subtitle,
        IReadOnlyList<HistoryPoint> points,
        string color)
    {
        var body = points.Count == 0
            ? """<p class="py-10 text-center text-sm text-base-content/50">No data yet.</p>"""
            : RenderBarChart(points, color);
        return RenderCard(title, subtitle, body);
    }

    private static string RenderBarChart(
        IReadOnlyList<HistoryPoint> points,
        string color)
    {
        const int padLeft = 40;
        const int padRight = 12;
        const int padTop = 16;
        const int padBottom = 32;
        var width = ChartWidth;
        var height = 220;
        var innerW = width - padLeft - padRight;
        var innerH = height - padTop - padBottom;

        var maxY = Math.Max(1, points.Max(p => p.Value));
        maxY *= 1.1;

        var count = points.Count;
        var slotW = innerW / (double)count;
        var barW = Math.Min(Math.Max(2, slotW * 0.7), 40);

        string Y(double v) => (padTop + innerH - v / maxY * innerH)
            .ToString("F1", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.Append($"""<svg viewBox="0 0 {width} {height}" class="w-full h-auto">""");

        for (var i = 0; i <= 4; i++)
        {
            var value = maxY * i / 4.0;
            var y = Y(value);
            sb.Append($"""<line x1="{padLeft}" y1="{y}" x2="{width - padRight}" y2="{y}" stroke="rgba(148,163,184,0.12)" stroke-width="1" />""");
            sb.Append($"""<text x="{padLeft - 6}" y="{y}" text-anchor="end" dominant-baseline="middle" fill="rgba(148,163,184,0.7)" font-size="11">{value:0}</text>""");
        }

        for (var i = 0; i < count; i++)
        {
            var v = points[i].Value;
            var barH = v / maxY * innerH;
            var x = padLeft + i * slotW + (slotW - barW) / 2;
            var y = Y(v);
            var label = points[i].At.ToLocalTime().ToString("dd MMM", CultureInfo.InvariantCulture);
            var tooltip = $"Session #{points[i].SessionId} · {points[i].At.ToLocalTime():dd MMM HH:mm} · {v:0} catches";
            sb.Append($"""<rect x="{x:F1}" y="{y}" width="{barW:F1}" height="{barH:F1}" fill="{color}" rx="2"><title>{WebUtility.HtmlEncode(tooltip)}</title></rect>""");

            var labelStep = Math.Max(1, count / 8);
            if (i % labelStep == 0)
                sb.Append($"""<text x="{(x + barW / 2):F1}" y="{height - 8}" text-anchor="middle" fill="rgba(148,163,184,0.7)" font-size="11">{WebUtility.HtmlEncode(label)}</text>""");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string RenderHorizontalBarsCard(
        string title,
        string subtitle,
        IReadOnlyList<HistoryBreakdown> items,
        Func<HistoryBreakdown, string> colorFor,
        bool showWeight = false,
        string weightLabel = "kg")
    {
        var body = items.Count == 0
            ? """<p class="py-10 text-center text-sm text-base-content/50">No data yet.</p>"""
            : RenderHorizontalBars(items, colorFor, showWeight, weightLabel);
        return RenderCard(title, subtitle, body);
    }

    private static string RenderHorizontalBars(
        IReadOnlyList<HistoryBreakdown> items,
        Func<HistoryBreakdown, string> colorFor,
        bool showWeight,
        string weightLabel)
    {
        var max = items.Max(i => i.Count);
        if (max == 0) max = 1;

        var sb = new StringBuilder();
        sb.Append("""<div class="space-y-2">""");
        foreach (var item in items)
        {
            var pct = item.Count / (double)max * 100;
            var color = colorFor(item);
            var label = WebUtility.HtmlEncode(TextCleanup.CleanFishName(item.Label));
            var right = showWeight
                ? $"{item.TotalWeightKg:N0} {WebUtility.HtmlEncode(weightLabel)}"
                : $"{item.Count:N0}";
            sb.Append($"""
                <div class="flex items-center gap-3 text-sm">
                  <div class="w-36 shrink-0 truncate text-right text-base-content/80" title="{label}">{label}</div>
                  <div class="relative h-6 flex-1 overflow-hidden rounded bg-base-200">
                    <div class="h-full rounded" style="width:{pct:F1}%;background:{color};"></div>
                    <span class="absolute inset-y-0 right-2 flex items-center text-xs font-medium text-base-content/90">{right}</span>
                  </div>
                </div>
                """);
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string RenderCard(string title, string subtitle, string body) => $"""
        <section class="card border border-base-300 bg-base-100 shadow-xl">
          <div class="card-body p-5">
            <div class="mb-3">
              <h2 class="card-title text-base">{WebUtility.HtmlEncode(title)}</h2>
              <p class="text-xs text-base-content/60">{WebUtility.HtmlEncode(subtitle)}</p>
            </div>
            {body}
          </div>
        </section>
        """;

    private static string FormatDuration(TimeSpan span) =>
        span.TotalDays >= 1 ? $"{(int)span.TotalDays}d {span.Hours}h"
        : span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes}m"
        : $"{span.Minutes}m";

    private static string GetRarityColor(string tag) =>
        tag.ToLowerInvariant() switch
        {
            "valuable" => "94a3b8",
            "trophy" => "eab308",
            "rare trophy" => "60a5fa",
            "rare" => "a855f7",
            _ => "64748b"
        };
}