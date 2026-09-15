using System.Globalization;
using System.Net;

namespace RF4Catches.Services;

public sealed record DashboardCatch(
    string Species,
    decimal? WeightKg,
    decimal? LengthCm,
    string? Rarity,
    string? ImagePath,
    DateTimeOffset CaughtAtUtc,
    int SessionId);

public sealed record DashboardData(
    int TotalCatches,
    decimal TotalWeightKg,
    decimal AverageWeightKg,
    int CatchesToday,
    int? ActiveSessionId,
    SessionDetails? ActiveSessionDetails,
    IReadOnlyList<DashboardCatch> RecentCatches);

public static class DashboardMarkup
{
    public static string RenderContent(DashboardData data)
    {
        var recentCards = data.RecentCatches.Count == 0
            ? """<div class="py-10 text-center text-base-content/60 sm:col-span-2 xl:col-span-3">No catches recorded yet.</div>"""
            : string.Join(Environment.NewLine, data.RecentCatches.Select(RenderCard));

        return $$"""
                 <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
                   {{StatCard("Total catches", data.TotalCatches.ToString("N0", CultureInfo.InvariantCulture), "All recorded catches")}}
                   {{StatCard("Total weight", $"{data.TotalWeightKg.ToString("N2", CultureInfo.InvariantCulture)} kg", "Combined catch weight")}}
                   {{StatCard("Average weight", $"{data.AverageWeightKg.ToString("N2", CultureInfo.InvariantCulture)} kg", "Average per catch")}}
                   {{StatCard("Caught today", data.CatchesToday.ToString("N0", CultureInfo.InvariantCulture), "Since midnight UTC")}}
                 </div>

                 <section class="card mt-6 border border-base-300 bg-base-100 shadow-xl">
                   <div class="card-body p-0">
                     <div class="flex items-center justify-between gap-4 px-6 pt-6">
                       <div>
                         <h2 class="card-title">Recent catches</h2>
                         <p class="text-sm text-base-content/60">Catches from the active session appear here.</p>
                       </div>
                       <span class="badge badge-success gap-2"><span class="h-2 w-2 rounded-full bg-success-content"></span>Live</span>
                     </div>
                     <div class="mt-4 grid gap-3 px-6 pb-6 sm:grid-cols-2 xl:grid-cols-3">
                       {{recentCards}}
                     </div>
                   </div>
                 </section>
                 """;
    }
    
    public static string RenderCaptureTest(SessionService.CaptureTestResult result)
{
    var ocr = result.Ocr;
    var parsed = result.Parsed;
    var confidence = ocr.Result.Confidence;
    var badgeClass = confidence >= 0.75f ? "badge-success"
        : confidence >= 0.5f ? "badge-warning"
        : "badge-error";

    var species = WebUtility.HtmlEncode(parsed.Species ?? "(none)");
    var weight = parsed.WeightKg is { } w ? $"{w.ToString("N3", CultureInfo.InvariantCulture)} kg" : "(none)";
    var length = parsed.LengthCm is { } l ? $"{l.ToString("N1", CultureInfo.InvariantCulture)} cm" : "(none)";
    var rarity = WebUtility.HtmlEncode(parsed.Rarity ?? "(none)");
    var speciesText = WebUtility.HtmlEncode(ocr.SpeciesText ?? "(n/a)");
    var detailsText = WebUtility.HtmlEncode(ocr.DetailsText ?? "(n/a)");
    var rawText = WebUtility.HtmlEncode(ocr.Result.Text);

    return $"""
        <div class="rounded-box border border-base-300 bg-base-100 p-4 shadow-xl">
          <div class="mb-2 flex items-center justify-between">
            <h3 class="font-semibold">Capture test</h3>
            <span class="badge {badgeClass}">{confidence.ToString("P0", CultureInfo.InvariantCulture)} confidence</span>
          </div>
          <p class="mb-2 text-sm">
            Species: <strong>{species}</strong> ·
            Weight: <strong>{weight}</strong> ·
            Length: <strong>{length}</strong> ·
            Rarity: <strong>{rarity}</strong>
          </p>
          <details class="text-sm">
            <summary class="cursor-pointer text-base-content/60">Raw OCR text</summary>
            <div class="mt-2 space-y-1 text-xs">
              <div><span class="text-base-content/50">Species crop:</span> {speciesText}</div>
              <div><span class="text-base-content/50">Details crop:</span> {detailsText}</div>
              <pre class="mt-2 whitespace-pre-wrap rounded bg-base-200 p-2">{rawText}</pre>
            </div>
          </details>
        </div>
        """;
}
    
    public static string RenderSession(DashboardData data)
    {
        return $$"""
                 <section class="mb-6 flex flex-col gap-4 rounded-box border border-base-300 bg-base-100 p-5 shadow-xl sm:flex-row sm:items-center sm:justify-between">
                   <div>
                     <div class="flex items-center gap-3">
                       <h2 class="text-lg font-semibold">Fishing session</h2>
                       {{SessionBadge(data.ActiveSessionId)}}
                     </div>
                     <p class="mt-1 text-sm text-base-content/60">Catches are grouped into the active session.</p>
                   </div>
                   <button class="btn btn-secondary"
                         hx-post="/api/capture/test"
                         hx-target="#capture-test-result"
                         hx-swap="innerHTML">
                   Capture test
                 </button>
                   <div class="flex flex-wrap gap-2">
                     <button class="btn btn-primary"
                             hx-post="/dashboard/session/new"
                             hx-swap="none">
                       {{(data.ActiveSessionId is null ? "Start session" : "Start new session")}}
                     </button>
                     {{(data.ActiveSessionId is not null
                         ? """<button class="btn btn-error" hx-post="/dashboard/session/end" hx-swap="none">End session</button>"""
                         : "")}}
                   </div>
                 </section>
                 {{SessionDetailsForm(data.ActiveSessionId, data.ActiveSessionDetails)}}
                 """;
    }
    
    private static string SessionDetailsForm(int? sessionId, SessionDetails? details)
    {
        if (sessionId is null) return "";
        details ??= new SessionDetails(null, null, null, null, null, null, null, null);
        return $$"""
                 <form id="session-details-form"
                       class="mb-6 rounded-box border border-base-300 bg-base-100 p-5 shadow-xl"
                       hx-post="/dashboard/session/details" hx-swap="none">
                   <div class="mb-4">
                     <h2 class="card-title">Session details</h2>
                     <p class="text-sm text-base-content/60">Record the setup and earnings for this fishing session.</p>
                   </div>
                   <div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
                     {{Input("Fishing method", "FishingMethod", details.FishingMethod, "e.g. float, feeder, spinning")}}
                     {{Input("Bait / lures", "Baits", details.Baits, "Separate multiple items with commas")}}
                     {{Input("Line clip", "LineClip", details.LineClip, "e.g. 18 m")}}
                     {{Input("Hook depth (cm)", "HookDepthCm", details.HookDepthCm?.ToString(CultureInfo.InvariantCulture), "e.g. 120")}}
                     {{Input("Map", "MapName", details.MapName, "Fishing location")}}
                     {{Input("Map coordinates", "MapCoordinates", details.MapCoordinates, "e.g. 42:18")}}
                     {{Input("Café silver", "CafeSilver", details.CafeSilver?.ToString(CultureInfo.InvariantCulture), "Optional sale amount")}}
                     {{Input("Market silver", "MarketSilver", details.MarketSilver?.ToString(CultureInfo.InvariantCulture), "Optional sale amount")}}
                   </div>
                   <button class="btn btn-secondary mt-4" type="submit">Save session details</button>
                 </form>
                 """;
    }
    
    private static string Input(string label, string name, string? value, string placeholder) =>
        $"""<label class="form-control"><span class="label-text">{WebUtility.HtmlEncode(label)}</span><input class="input input-bordered input-sm" name="{name}" value="{WebUtility.HtmlEncode(value ?? "")}" placeholder="{WebUtility.HtmlEncode(placeholder)}"></label>""";

    private static string SessionBadge(int? sessionId) =>
        sessionId is { } id
            ? $"""<span class="badge badge-success">Active · #{id}</span>"""
            : """<span class="badge badge-ghost">No active session</span>""";

    private static string StatCard(string title, string value, string description) => $$"""
          <div class="stat rounded-box border border-base-300 bg-base-100 shadow-xl">
            <div class="stat-title">{{title}}</div>
            <div class="stat-value text-primary">{{value}}</div>
            <div class="stat-desc">{{description}}</div>
          </div>
          """;
    
    private static string RenderCard(DashboardCatch catchRecord)
    {
        var species = WebUtility.HtmlEncode(TextCleanup.CleanFishName(catchRecord.Species));
        var weight = catchRecord.WeightKg is { } weightKg
            ? $"{CatchWeight.NormalizeKg(weightKg).ToString("N2", CultureInfo.InvariantCulture)} kg"
            : "—";
        var length = catchRecord.LengthCm is { } lengthCm
            ? $"{lengthCm.ToString("N1", CultureInfo.InvariantCulture)} cm"
            : "—";
        var rarity = RenderRarity(catchRecord.Rarity);
        var rarityCardClass = RarityCardClass(catchRecord.Rarity);
        var caughtAt = catchRecord.CaughtAtUtc.ToLocalTime().ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
        var image = RenderImage(catchRecord.ImagePath, catchRecord.Species);
        return $$"""
          <article class="catch-card {{rarityCardClass}} card border border-base-300 bg-base-200 shadow-sm" data-balatro-card>
            {{image}}
            <div class="card-body gap-1 p-4">
              <div class="flex items-start justify-between gap-2">
                <h3 class="font-semibold">{{species}}</h3>
                {{rarity}}
              </div>
              <div class="text-sm text-base-content/70">{{weight}} · {{length}}</div>
              <div class="text-xs text-base-content/50">{{caughtAt}} · session #{{catchRecord.SessionId}}</div>
            </div>
          </article>
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

    private static string RenderRarity(string? rarity)
    {
        if (string.IsNullOrWhiteSpace(rarity)) return "—";
        var encoded = WebUtility.HtmlEncode(rarity);
        var style = rarity.Equals("Valuable", StringComparison.OrdinalIgnoreCase)
            ? "badge-success"
            : rarity.Equals("Rare Trophy", StringComparison.OrdinalIgnoreCase)
                ? "badge-info"
                : rarity.Equals("Trophy", StringComparison.OrdinalIgnoreCase)
                    ? "badge-warning"
                    : rarity.Equals("Rare", StringComparison.OrdinalIgnoreCase)
                        ? "badge-secondary"
                        : "badge-ghost";
        return $"""<span class="badge {style}">{encoded}</span>""";
    }

    private static string RarityCardClass(string? rarity) =>
        rarity is not null &&
        (rarity.Equals("Rare", StringComparison.OrdinalIgnoreCase) ||
         rarity.Equals("Trophy", StringComparison.OrdinalIgnoreCase) ||
         rarity.Equals("Rare Trophy", StringComparison.OrdinalIgnoreCase))
            ? "prism-card"
            : "";
}
