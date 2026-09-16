using Microsoft.EntityFrameworkCore;
using RF4Catches.Models;

namespace RF4Catches.Services;

public sealed class SessionService(
    IDbContextFactory<Data.AppDbContext> dbContextFactory,
    ScreenCaptureService screenCaptureService,
    OcrService ocrService,
    RoiProfileStore roiProfiles,
    CatchOcrParser catchOcrParser,
    FishNameMatcher fishNameMatcher,
    IWebHostEnvironment environment,
    ILogger<SessionService> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int? _activeSessionId;
    public int? ActiveSessionId => _activeSessionId;

    private SessionDetails? _cachedActiveDetails;
    private int? _cachedDetailsSessionId;

    public sealed record CaptureTestResult(CatchCardOcr Ocr, ParsedCatch Parsed);

    public async Task<CaptureTestResult> TestCaptureAsync(CancellationToken cancellationToken = default)
    {
        var capturePath = await screenCaptureService.CaptureVirtualScreenAsync(cancellationToken, persist: false);
        try
        {
            var profiles = await roiProfiles.GetAllAsync(cancellationToken);
            var catchOcr = ReadCatchFromScreen(capturePath, profiles);

            if (catchOcr is null)
                throw new InvalidOperationException("No catch card detected (icons not found).");

            var rarityRegion = profiles
                .FirstOrDefault(p => string.Equals(p.Name, "Catch rarity", StringComparison.OrdinalIgnoreCase))
                ?.Region;

            var detectedRarity = rarityRegion is null
                ? null
                : screenCaptureService.DetectTagRarities(capturePath, rarityRegion).ToDisplayString();

            var parsed = catchOcr.IsSplit
                ? catchOcrParser.ParseFields(catchOcr.SpeciesText!, catchOcr.DetailsText!, detectedRarity)
                : catchOcrParser.Parse(catchOcr.Result.Text, detectedRarity);

            return new CaptureTestResult(catchOcr, parsed);
        }
        finally
        {
            screenCaptureService.TryDeleteCapture(capturePath);
        }
    }

    public async Task SavePendingReviewAsync(
        string capturePath,
        OcrResult ocr,
        string reason,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.PendingReviews.Add(new PendingReview
        {
            RawOcrText = ocr.Text,
            OcrConfidence = ocr.Confidence,
            CapturePath = capturePath,
            Reason = reason
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateDetailsAsync(SessionDetails details, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_activeSessionId is not { } sessionId)
                throw new InvalidOperationException("There is no active fishing session.");

            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var session = await db.FishingSessions.SingleAsync(item => item.Id == sessionId, cancellationToken);
            session.FishingMethod = details.FishingMethod;
            session.Baits = details.Baits;
            session.LineClip = details.LineClip;
            session.HookDepthCm = details.HookDepthCm;
            session.MapName = details.MapName;
            session.MapCoordinates = details.MapCoordinates;
            session.CafeSilver = details.CafeSilver;
            session.MarketSilver = details.MarketSilver;
            await db.SaveChangesAsync(cancellationToken);

            // Details changed — invalidate cache so the dashboard sees the new values.
            _cachedActiveDetails = null;
            _cachedDetailsSessionId = null;
        }
        finally { _gate.Release(); }
    }

    public async Task<SessionDetails?> GetActiveDetailsAsync(
        IDbContextFactory<Data.AppDbContext> dbContextFactory,
        CancellationToken cancellationToken = default)
    {
        if (_activeSessionId is not { } sessionId)
        {
            _cachedActiveDetails = null;
            _cachedDetailsSessionId = null;
            return null;
        }

        if (_cachedDetailsSessionId == sessionId && _cachedActiveDetails is not null)
            return _cachedActiveDetails;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var details = await db.FishingSessions
            .Where(s => s.Id == sessionId)
            .Select(s => new SessionDetails(
                s.FishingMethod, s.Baits, s.LineClip, s.HookDepthCm,
                s.MapName, s.MapCoordinates, s.CafeSilver, s.MarketSilver))
            .FirstOrDefaultAsync(cancellationToken);

        _cachedActiveDetails = details;
        _cachedDetailsSessionId = sessionId;
        return details;
    }

    public async Task<bool> DeleteCatchAsync(int catchId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var catchRecord = await db.Catches
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(c => c.Id == catchId, cancellationToken);

            if (catchRecord is null || catchRecord.IsDeleted)
                return false;

            catchRecord.IsDeleted = true;
            catchRecord.DeletedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Catch {CatchId} ({Species}) soft-deleted.", catchId, catchRecord.Species);
            return true;
        }
        finally { _gate.Release(); }
    }
    
    public async Task<bool> RestoreCatchAsync(int catchId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var catchRecord = await db.Catches
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(c => c.Id == catchId, cancellationToken);

            if (catchRecord is null || !catchRecord.IsDeleted)
                return false;

            catchRecord.IsDeleted = false;
            catchRecord.DeletedAtUtc = null;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Catch {CatchId} ({Species}) restored.", catchId, catchRecord.Species);
            return true;
        }
        finally { _gate.Release(); }
    }
    
    public async Task<bool> DeleteSessionAsync(int sessionId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_activeSessionId == sessionId)
                throw new InvalidOperationException("Cannot delete the active session. End it first.");

            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var session = await db.FishingSessions
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

            if (session is null || session.IsDeleted)
                return false;

            session.IsDeleted = true;
            session.DeletedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Session {SessionId} soft-deleted.", sessionId);
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> RestoreSessionAsync(int sessionId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var session = await db.FishingSessions
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

            if (session is null || !session.IsDeleted)
                return false;

            session.IsDeleted = false;
            session.DeletedAtUtc = null;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Session {SessionId} restored.", sessionId);
            return true;
        }
        finally { _gate.Release(); }
    }
    
    
    public async Task<FishingSession> StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_activeSessionId is not null) throw new InvalidOperationException("A fishing session is already active.");
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var session = new FishingSession();
            db.FishingSessions.Add(session);
            await db.SaveChangesAsync(cancellationToken);
            _activeSessionId = session.Id;

            // New session — invalidate cache.
            _cachedActiveDetails = null;
            _cachedDetailsSessionId = null;

            logger.LogInformation("Fishing session {SessionId} started.", session.Id);
            return session;
        }
        finally { _gate.Release(); }
    }

    public async Task<FishingSession> StartNewAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            if (_activeSessionId is { } previousSessionId)
            {
                var previousSession = await db.FishingSessions.SingleAsync(s => s.Id == previousSessionId, cancellationToken);
                previousSession.EndedAtUtc = DateTimeOffset.UtcNow;
            }

            var session = new FishingSession();
            db.FishingSessions.Add(session);
            await db.SaveChangesAsync(cancellationToken);
            _activeSessionId = session.Id;

            // New session — invalidate cache.
            _cachedActiveDetails = null;
            _cachedDetailsSessionId = null;

            logger.LogInformation("New fishing session {SessionId} started.", session.Id);
            return session;
        }
        finally { _gate.Release(); }
    }

    public async Task<FishingSession> EndAndProcessAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_activeSessionId is not { } sessionId)
                throw new InvalidOperationException("There is no active fishing session.");

            var capturePath = await screenCaptureService.CaptureVirtualScreenAsync(
                cancellationToken, persist: false);

            try
            {
                var profiles = await roiProfiles.GetAllAsync(cancellationToken);

                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                var session = await db.FishingSessions.SingleAsync(s => s.Id == sessionId, cancellationToken);

                session.EndedAtUtc = DateTimeOffset.UtcNow;

                var catchOcr = ReadCatchFromScreen(capturePath, profiles);

                if (catchOcr is null)
                {
                    await db.SaveChangesAsync(cancellationToken);
                    _activeSessionId = null;

                    // Session ended — invalidate cache.
                    _cachedActiveDetails = null;
                    _cachedDetailsSessionId = null;

                    logger.LogInformation("Fishing session {SessionId} ended (no catch card detected).", sessionId);
                    return session;
                }

                var ocr = catchOcr.Result;
                session.RawOcrText = ocr.Text;
                session.OcrConfidence = ocr.Confidence;

                var rarityRegion = profiles
                    .FirstOrDefault(p => string.Equals(p.Name, "Catch rarity", StringComparison.OrdinalIgnoreCase))
                    ?.Region;

                var detectedRarity = rarityRegion is null
                    ? null
                    : screenCaptureService.DetectTagRarities(capturePath, rarityRegion).ToDisplayString();

                var parsedCatch = catchOcr.IsSplit
                    ? catchOcrParser.ParseFields(catchOcr.SpeciesText!, catchOcr.DetailsText!, detectedRarity)
                    : catchOcrParser.Parse(ocr.Text, detectedRarity);

                if (parsedCatch.HasCatchDetails)
                {
                    var fishImageProfile = profiles
                        .FirstOrDefault(item => string.Equals(item.Name, "Fish image", StringComparison.OrdinalIgnoreCase));

                    db.Catches.Add(new Catch
                    {
                        FishingSessionId = session.Id,
                        Species = parsedCatch.Species!,
                        WeightKg = parsedCatch.WeightKg,
                        LengthCm = parsedCatch.LengthCm,
                        Rarity = parsedCatch.Rarity,
                        ImagePath = fishImageProfile is null
                            ? null
                            : await screenCaptureService.PersistCatchImageAsync(
                                capturePath, fishImageProfile.Region, cancellationToken),
                        RawOcrText = ocr.Text
                    });
                }
                else
                {
                    session.CapturePath = await screenCaptureService.PersistCaptureAsync(
                        capturePath, cancellationToken);
                    await SavePendingReviewAsync(
                        session.CapturePath, ocr, "Session end: parse produced no catch details", cancellationToken);
                }

                await db.SaveChangesAsync(cancellationToken);
                _activeSessionId = null;

                // Session ended — invalidate cache.
                _cachedActiveDetails = null;
                _cachedDetailsSessionId = null;

                logger.LogInformation(
                    "Fishing session {SessionId} ended with OCR confidence {Confidence:P0}.",
                    sessionId, ocr.Confidence);
                return session;
            }
            finally
            {
                screenCaptureService.TryDeleteCapture(capturePath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FishingSession> CaptureAndProcessAsync(CancellationToken cancellationToken = default)
    {
        if (ActiveSessionId is null)
            await StartAsync(cancellationToken);

        return await EndAndProcessAsync(cancellationToken);
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

        foreach (var candidate in candidates)
        {
            var cleaned = TextCleanup.CleanFishName(candidate.Text);
            if (cleaned.Length < 3) continue;
            var normalized = fishNameMatcher.Normalize(cleaned);
            if (!string.Equals(normalized, cleaned, StringComparison.Ordinal))
                return candidate;
        }

        return candidates[0];
    }

    private static OcrService.OcrCandidate? PickBestWithUnit(
        IReadOnlyList<OcrService.OcrCandidate> candidates,
        params string[] units)
    {
        if (candidates.Count == 0) return null;

        var withUnit = candidates.FirstOrDefault(c =>
            units.Any(u => c.Text.Contains(u, StringComparison.OrdinalIgnoreCase)));
        if (withUnit is not null) return withUnit;

        var original = candidates.FirstOrDefault(c => c.IsOriginal && c.Text.Any(char.IsDigit));
        if (original is not null) return original;

        return candidates.FirstOrDefault(c => c.Text.Any(char.IsDigit)) ?? candidates[0];
    }

    public async Task<bool> ProcessDetectedCatchAsync(
        string capturePath,
        CatchCardOcr catchOcr,
        CancellationToken cancellationToken = default)
    {
        var profiles = await roiProfiles.GetAllAsync(cancellationToken);
        var rarityProfile = profiles
            .FirstOrDefault(item => string.Equals(item.Name, "Catch rarity", StringComparison.OrdinalIgnoreCase));
        var detectedRarity = rarityProfile is null
            ? null
            : screenCaptureService.DetectTagRarities(capturePath, rarityProfile.Region).ToDisplayString();
        var parsedCatch = catchOcr.IsSplit
            ? catchOcrParser.ParseFields(catchOcr.SpeciesText!, catchOcr.DetailsText!, detectedRarity)
            : catchOcrParser.Parse(catchOcr.Result.Text, detectedRarity);
        var ocr = catchOcr.Result;
        if (!parsedCatch.HasCatchDetails) return false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            FishingSession session;
            if (_activeSessionId is { } sessionId)
            {
                session = await db.FishingSessions.SingleAsync(item => item.Id == sessionId, cancellationToken);
            }
            else
            {
                session = new FishingSession();
                db.FishingSessions.Add(session);
            }

            var fishImageProfile = profiles
                .FirstOrDefault(item => string.Equals(item.Name, "Fish image", StringComparison.OrdinalIgnoreCase));
            var imagePath = fishImageProfile is null
                ? null
                : await screenCaptureService.PersistCatchImageAsync(capturePath, fishImageProfile.Region, cancellationToken);

            session.Catches.Add(new Catch
            {
                Species = parsedCatch.Species!,
                WeightKg = parsedCatch.WeightKg,
                LengthCm = parsedCatch.LengthCm,
                Rarity = parsedCatch.Rarity,
                ImagePath = imagePath,
                RawOcrText = ocr.Text,
                FishingSession = session
            });
            session.RawOcrText = ocr.Text;
            session.OcrConfidence = ocr.Confidence;
            await db.SaveChangesAsync(cancellationToken);

            // If this call created a new session (no active one), invalidate cache.
            if (_activeSessionId != session.Id)
            {
                _cachedActiveDetails = null;
                _cachedDetailsSessionId = null;
            }
            _activeSessionId = session.Id;

            logger.LogInformation("Automatically detected catch {Species} ({WeightKg} kg) in session {SessionId}.",
                parsedCatch.Species, parsedCatch.WeightKg, session.Id);
            return true;
        }
        finally { _gate.Release(); }
    }
}