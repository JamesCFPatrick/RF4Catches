using Microsoft.Extensions.Options;
using RF4Catches.Services;

namespace RF4Catches.HostedServices;

public sealed class AutomaticDetectionOptions
{
    public bool Enabled { get; init; } = true;
    public int IntervalMilliseconds { get; init; } = 250;
    public int RequiredConsecutiveDetections { get; init; } = 1;
    public int SameSignatureCooldownMilliseconds { get; init; } = 7500;
}

public sealed class AutomaticDetectionService(
    ScreenCaptureService screenCaptureService,
    OcrService ocrService,
    RoiProfileStore roiProfiles,
    CatchOcrParser catchOcrParser,
    SessionService sessionService,
    FishNameMatcher fishNameMatcher,
    IWebHostEnvironment environment,
    IOptions<AutomaticDetectionOptions> options,
    ILogger<AutomaticDetectionService> logger) : BackgroundService
{
    private readonly AutomaticDetectionOptions _options = options.Value;
    private string? _candidateSignature;
    private int _candidateCount;
    private string? _lastProcessedSignature;
    private DateTimeOffset? _lastProcessedAtUtc;
    private DateTimeOffset _lastRejectionLogAtUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastPendingReviewAtUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastMissingProfileLogAtUtc = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Automatic catch detection is disabled.");
            return;
        }

        var interval = TimeSpan.FromMilliseconds(Math.Max(250, _options.IntervalMilliseconds));
        var requiredDetections = Math.Max(1, _options.RequiredConsecutiveDetections);
        var sameSignatureCooldown = TimeSpan.FromMilliseconds(Math.Max(1000, _options.SameSignatureCooldownMilliseconds));
        logger.LogInformation("Automatic catch detection enabled with a {Interval} ms interval.", interval.TotalMilliseconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DetectAsync(requiredDetections, sameSignatureCooldown, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Automatic catch detection failed.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task DetectAsync(
        int requiredDetections,
        TimeSpan sameSignatureCooldown,
        CancellationToken cancellationToken)
    {
        if (sessionService.ActiveSessionId is null)
        {
            _candidateSignature = null;
            _candidateCount = 0;
            _lastProcessedSignature = null;
            _lastProcessedAtUtc = null;
            return;
        }

        // Temp file — we own its lifetime here.
        var capturePath = await screenCaptureService.CaptureVirtualScreenAsync(
            cancellationToken, persist: false);

        try
        {
            var profiles = await roiProfiles.GetAllAsync(cancellationToken);

            var hasRequiredProfiles =
                profiles.Any(p => string.Equals(p.Name, "Catch species", StringComparison.OrdinalIgnoreCase)) &&
                profiles.Any(p => string.Equals(p.Name, "Catch info", StringComparison.OrdinalIgnoreCase));

            if (!hasRequiredProfiles)
            {
                if (DateTimeOffset.UtcNow - _lastMissingProfileLogAtUtc >= TimeSpan.FromSeconds(30))
                {
                    logger.LogError(
                        "Automatic catch detection is paused because 'Catch species' and 'Catch info' profiles are required.");
                    _lastMissingProfileLogAtUtc = DateTimeOffset.UtcNow;
                }
                return;
            }

            var catchOcr = ReadCatchFromScreen(capturePath, profiles);
            if (catchOcr is null)
                return;

            var ocr = catchOcr.Result;

            var rarityRegion = profiles
                .FirstOrDefault(p => string.Equals(p.Name, "Catch rarity", StringComparison.OrdinalIgnoreCase))
                ?.Region;

            var detectedRarity = rarityRegion is null
                ? null
                : screenCaptureService.DetectTagRarities(capturePath, rarityRegion).ToDisplayString();

            var parsedCatch = catchOcr.IsSplit
                ? catchOcrParser.ParseFields(catchOcr.SpeciesText!, catchOcr.DetailsText!, detectedRarity)
                : catchOcrParser.Parse(ocr.Text, detectedRarity);

            if (!parsedCatch.HasCatchDetails)
            {
                if (DateTimeOffset.UtcNow - _lastRejectionLogAtUtc >= TimeSpan.FromSeconds(10))
                {
                    logger.LogWarning(
                        "Catch card was not accepted. OCR confidence: {Confidence:P0}; parsed species: {Species}; parsed weight: {WeightKg} kg; text: {Text}",
                        ocr.Confidence,
                        parsedCatch.Species ?? "(none)",
                        parsedCatch.WeightKg?.ToString() ?? "(none)",
                        ocr.Text.Length > 160 ? ocr.Text[..160] : ocr.Text);
                    _lastRejectionLogAtUtc = DateTimeOffset.UtcNow;
                }

                if (DateTimeOffset.UtcNow - _lastPendingReviewAtUtc >= TimeSpan.FromSeconds(10))
                {
                    // Only here do we persist — the review references this file by path.
                    var persistedPath = await screenCaptureService.PersistCaptureAsync(
                        capturePath, cancellationToken);
                    await sessionService.SavePendingReviewAsync(
                        persistedPath, ocr, "Required catch fields were not parsed", cancellationToken);
                    _lastPendingReviewAtUtc = DateTimeOffset.UtcNow;
                }

                _candidateSignature = null;
                _candidateCount = 0;
                _lastProcessedSignature = null;
                _lastProcessedAtUtc = null;
                return;
            }

            var signature = $"{parsedCatch.Species}|{parsedCatch.WeightKg}|{parsedCatch.LengthCm}|{parsedCatch.Rarity}";

            if (!string.Equals(signature, _candidateSignature, StringComparison.Ordinal))
            {
                _candidateSignature = signature;
                _candidateCount = 1;
                return;
            }

            _candidateCount++;

            var sameSignatureRecentlyProcessed =
                string.Equals(signature, _lastProcessedSignature, StringComparison.Ordinal) &&
                _lastProcessedAtUtc is { } lastProcessedAt &&
                DateTimeOffset.UtcNow - lastProcessedAt < sameSignatureCooldown;

            if (_candidateCount < requiredDetections || sameSignatureRecentlyProcessed)
                return;

            // No persist — process against the temp path, let the finally clean up.
            if (await sessionService.ProcessDetectedCatchAsync(capturePath, catchOcr, cancellationToken))
            {
                _lastProcessedSignature = signature;
                _lastProcessedAtUtc = DateTimeOffset.UtcNow;
            }
        }
        finally
        {
            // No-op if the pending-review path already moved the file.
            screenCaptureService.TryDeleteCapture(capturePath);
        }
    }

    private CatchCardOcr? ReadCatchFromScreen(string capturePath, IReadOnlyList<RoiProfile> profiles)
    {
        var species = profiles.FirstOrDefault(p =>
                          string.Equals(p.Name, "Catch species", StringComparison.OrdinalIgnoreCase))
                      ?? throw new InvalidOperationException("A 'Catch species' ROI profile is required.");
        var info = profiles.FirstOrDefault(p =>
                       string.Equals(p.Name, "Catch info", StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException("A 'Catch info' ROI profile is required.");

        var templatesDir = Path.Combine(environment.ContentRootPath, "data", "templates");
        var bag = screenCaptureService.FindIcon(capturePath, info.Region, Path.Combine(templatesDir, "bag.png"));
        var ruler = screenCaptureService.FindIcon(capturePath, info.Region, Path.Combine(templatesDir, "ruler.png"));

        if (bag is null || ruler is null)
            return null;

        const double weightOffsetX = 0.01;
        const double lengthOffsetX = 0.01;
        const double regionW = 0.060;
        const double regionH = 0.030;
        const double regionOffsetY = 0.010;

        var weightRegion = new OcrRegion(bag.Value.X + weightOffsetX, bag.Value.Y - regionOffsetY, regionW, regionH);
        var lengthRegion = new OcrRegion(ruler.Value.X + lengthOffsetX, ruler.Value.Y - regionOffsetY, regionW, regionH);

        var speciesCandidates = ocrService.ReadRegionCandidates(
            capturePath, species.Region,
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz '-.",
            Tesseract.PageSegMode.SingleLine);
        var weightCandidates = ocrService.ReadRegionCandidates(
            capturePath, weightRegion,
            "0123456789.,kg ",
            Tesseract.PageSegMode.SingleLine);
        var lengthCandidates = ocrService.ReadRegionCandidates(
            capturePath, lengthRegion,
            "0123456789.,cm ",
            Tesseract.PageSegMode.SingleLine);

        var speciesPick = PickBestSpecies(speciesCandidates);
        var weightPick = PickBestWithUnit(weightCandidates, "kg", "g");
        var lengthPick = PickBestWithUnit(lengthCandidates, "cm");

        var speciesText = speciesPick?.Text ?? "";
        var weightText = weightPick?.Text ?? "";
        var lengthText = lengthPick?.Text ?? "";

        var confidence = Math.Min(
            speciesPick?.Confidence ?? 0f,
            Math.Min(weightPick?.Confidence ?? 0f, lengthPick?.Confidence ?? 0f));

        var detailsText = $"{weightText} {lengthText}".Trim();
        return new CatchCardOcr(
            new OcrResult($"{speciesText}\n{detailsText}".Trim(), confidence),
            speciesText,
            weightText,
            lengthText);
    }

    private OcrService.OcrCandidate? PickBestSpecies(IReadOnlyList<OcrService.OcrCandidate> candidates)
    {
        if (candidates.Count == 0) return null;

        // Prefer a candidate that resolves to a real catalog fish after normalization.
        foreach (var candidate in candidates)
        {
            var cleaned = TextCleanup.CleanFishName(candidate.Text);
            if (cleaned.Length < 3) continue;
            var normalized = fishNameMatcher.Normalize(cleaned);
            if (!string.Equals(normalized, cleaned, StringComparison.Ordinal))
                return candidate;
        }

        // Nothing matched — fall back to highest confidence.
        return candidates[0];
    }

    private static OcrService.OcrCandidate? PickBestWithUnit(
        IReadOnlyList<OcrService.OcrCandidate> candidates,
        params string[] units)
    {
        if (candidates.Count == 0) return null;

        // 1. Prefer any candidate that contains a unit token (kg, g, cm).
        var withUnit = candidates.FirstOrDefault(c =>
            units.Any(u => c.Text.Contains(u, StringComparison.OrdinalIgnoreCase)));
        if (withUnit is not null) return withUnit;

        // 2. Otherwise prefer the original if it contains a digit.
        var original = candidates.FirstOrDefault(c => c.IsOriginal && c.Text.Any(char.IsDigit));
        if (original is not null) return original;

        // 3. Fall back to any digit-containing candidate.
        return candidates.FirstOrDefault(c => c.Text.Any(char.IsDigit)) ?? candidates[0];
    }
}