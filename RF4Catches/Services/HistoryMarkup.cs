using System.Globalization;
using System.Net;

namespace RF4Catches.Services;

public sealed record HistoryCatch(
    int Id,
    string Species,
    decimal? WeightKg,
    decimal? LengthCm,
    string? Rarity,
    string? ImagePath,
    DateTimeOffset CaughtAtUtc);

public sealed record HistorySession(
    int Id,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    SessionDetails Details,
    IReadOnlyList<HistoryCatch> Catches);

public static class HistoryMarkup
{
    public static string Render(IReadOnlyList<HistorySession> sessions)
    {
        var content = sessions.Count == 0
            ? """<div class="rounded-box border border-base-300 bg-base-100 p-10 text-center text-base-content/60">No historical catches recorded yet.</div>"""
            : string.Join(Environment.NewLine, sessions.Select(RenderSession));

        return $"""
        <div class="space-y-5">
          {content}
        </div>
        """;
    }

    private static string RenderSession(HistorySession session)
    {
        var totalWeight = session.Catches.Sum(catchRecord =>
            catchRecord.WeightKg is { } weightKg ? CatchWeight.NormalizeKg(weightKg) : 0m);
        var ended = session.EndedAtUtc is { } endedAt
            ? endedAt.ToLocalTime().ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture)
            : "Active";
        var catches = string.Join(Environment.NewLine, session.Catches
            .OrderByDescending(catchRecord => catchRecord.CaughtAtUtc)
            .Select(RenderCatch));
        var details = RenderDetails(session.Details);

        return $"""
        <section class="card border border-base-300 bg-base-100 shadow-xl">
          <div class="card-body p-0">
            <div class="flex flex-col gap-3 border-b border-base-300 p-6 sm:flex-row sm:items-center sm:justify-between">
              <div>
                <div class="flex items-center gap-3">
                  <h2 class="card-title">Session #{session.Id}</h2>
                  {(session.EndedAtUtc is null ? """<span class="badge badge-success">Active</span>""" : "")}
                </div>
                <p class="mt-1 text-sm text-base-content/60">
                  {session.StartedAtUtc.ToLocalTime().ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture)} –
                  {ended}
                </p>
              </div>
              <div class="flex gap-2">
                <span class="badge badge-primary">{session.Catches.Count} catches</span>
                <span class="badge badge-ghost">{totalWeight.ToString("N2", CultureInfo.InvariantCulture)} kg</span>
              </div>
            </div>
            <div class="grid gap-3 p-6 sm:grid-cols-2 xl:grid-cols-3">
              {catches}
            </div>
            {details}
          </div>
        </section>
        """;
    }

    private static string RenderDetails(SessionDetails details)
    {
        var values = new[]
        {
            ("Method", details.FishingMethod),
            ("Bait / lures", details.Baits),
            ("Line clip", details.LineClip),
            ("Hook depth", details.HookDepthCm is { } depth ? $"{depth.ToString("N0", CultureInfo.InvariantCulture)} cm" : null),
            ("Map", details.MapName),
            ("Coordinates", details.MapCoordinates),
            ("Café silver", details.CafeSilver is { } cafe ? cafe.ToString("N2", CultureInfo.InvariantCulture) : null),
            ("Market silver", details.MarketSilver is { } market ? market.ToString("N2", CultureInfo.InvariantCulture) : null)
        }.Where(item => !string.IsNullOrWhiteSpace(item.Item2)).ToList();
        if (values.Count == 0) return "";
        var items = string.Join("", values.Select(item =>
            $"""<div><dt class="text-xs uppercase text-base-content/50">{WebUtility.HtmlEncode(item.Item1)}</dt><dd>{WebUtility.HtmlEncode(item.Item2!)}</dd></div>"""));
        return $"""<dl class="grid gap-3 border-t border-base-300 px-6 py-4 text-sm sm:grid-cols-2 lg:grid-cols-4">{items}</dl>""";
    }

    private static string RenderCatch(HistoryCatch catchRecord)
    {
        var species = WebUtility.HtmlEncode(TextCleanup.CleanFishName(catchRecord.Species));
        var weight = catchRecord.WeightKg is { } weightKg
            ? weightKg < 1m
                ? $"{(weightKg * 1000m).ToString("0.#", CultureInfo.InvariantCulture)} g"
                : $"{weightKg.ToString("0.###", CultureInfo.InvariantCulture)} kg"
            : "—";
        var length = catchRecord.LengthCm is { } lengthCm
            ? $"{lengthCm.ToString("N1", CultureInfo.InvariantCulture)} cm"
            : "—";
        var rarity = RarityMarkup.RenderBadges(catchRecord.Rarity);
        var rarityCardClass = RarityMarkup.CardClass(catchRecord.Rarity);
        var caughtAt = catchRecord.CaughtAtUtc.ToLocalTime().ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
        var image = RenderImage(catchRecord.ImagePath, catchRecord.Species);

        return $"""
                <div class="catch-card-wrapper flex h-full flex-col gap-1" data-catch-wrapper>
                  <div class="flex h-8 justify-end px-1">
                    {CatchCardActions.DeleteButton(catchRecord.Id)}
                  </div>
                  <article class="catch-card {rarityCardClass} card flex-1 border border-base-300 bg-base-200 shadow-sm" data-balatro-card>
                    {image}
                    <div class="card-body gap-1 p-4">
                      <div class="flex items-start justify-between gap-2">
                        <h3 class="line-clamp-2 min-h-[2.5rem] min-w-0 break-words font-semibold">{species}</h3>
                        <div class="flex shrink-0 flex-col items-end gap-1">{rarity}</div>
                      </div>
                      <div class="text-sm text-base-content/70">{weight} · {length}</div>
                      <div class="text-xs text-base-content/50">{caughtAt}</div>
                    </div>
                  </article>
                </div>
                """;
    }

    private static string RenderImage(string? imagePath, string species)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return """<div class="flex h-40 w-full items-center justify-center bg-base-300 text-xs text-base-content/40">No image</div>""";

        var safePath = WebUtility.HtmlEncode(imagePath);
        var safeAlt = WebUtility.HtmlEncode(TextCleanup.CleanFishName(species));
        return $"""<div class="flex h-40 w-full items-center justify-center bg-base-300"><img class="max-h-full max-w-full object-contain" src="/api/catch-image/{safePath}" alt="{safeAlt}"></div>""";
    }
}
