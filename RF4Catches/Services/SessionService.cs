namespace RF4Catches.Services;

public sealed class SessionService(
    Microsoft.EntityFrameworkCore.IDbContextFactory<Data.AppDbContext> dbContextFactory,
    ScreenCaptureService screenCaptureService,
    OcrService ocrService,
    RoiProfileStore roiProfiles,
    CatchOcrParser catchOcrParser,
    IWebHostEnvironment environment,
    ILogger<SessionService> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int? _activeSessionId;
    public int? ActiveSessionId => _activeSessionId;
    
    public sealed record CaptureTestResult(CatchCardOcr Ocr, ParsedCatch Parsed);

    public async Task<CaptureTestResult> TestCaptureAsync(CancellationToken cancellationToken = default)
    {
        var capturePath = await screenCaptureService.CaptureVirtualScreenAsync(cancellationToken, persist: false);
        try
        {
            var profiles = await roiProfiles.GetAllAsync(cancellationToken);
            var catchOcr = ReadCatchFromScreen(capturePath, profiles);
            var rarityRegion = profiles
                .FirstOrDefault(p => string.Equals(p.Name, "Catch rarity", StringComparison.OrdinalIgnoreCase))
                ?.Region;
            var detectedRarity = rarityRegion is null
                ? null
                : screenCaptureService.DetectTagRarity(capturePath, rarityRegion);
            var parsed = catchOcr.IsSplit
                ? catchOcrParser.ParseFields(catchOcr.SpeciesText!, catchOcr.DetailsText!, detectedRarity)
                : catchOcrParser.Parse(catchOcr.Result.Text, detectedRarity);
            return new CaptureTestResult(catchOcr, parsed);
        }
        finally
        {
            screenCaptureService.DeleteCapture(capturePath);
        }
    }
    
    public async Task SavePendingReviewAsync(
        string capturePath,
        OcrResult ocr,
        string reason,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.PendingReviews.Add(new Models.PendingReview
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
            var session = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
                db.FishingSessions, item => item.Id == sessionId, cancellationToken);
            session.FishingMethod = details.FishingMethod;
            session.Baits = details.Baits;
            session.LineClip = details.LineClip;
            session.HookDepthCm = details.HookDepthCm;
            session.MapName = details.MapName;
            session.MapCoordinates = details.MapCoordinates;
            session.CafeSilver = details.CafeSilver;
            session.MarketSilver = details.MarketSilver;
            await db.SaveChangesAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task<Models.FishingSession> StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_activeSessionId is not null) throw new InvalidOperationException("A fishing session is already active.");
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var session = new Models.FishingSession();
            db.FishingSessions.Add(session);
            await db.SaveChangesAsync(cancellationToken);
            _activeSessionId = session.Id;
            logger.LogInformation("Fishing session {SessionId} started.", session.Id);
            return session;
        }
        finally { _gate.Release(); }
    }

    public async Task<Models.FishingSession> StartNewAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            if (_activeSessionId is { } previousSessionId)
            {
                var previousSession = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
                    db.FishingSessions, s => s.Id == previousSessionId, cancellationToken);
                previousSession.EndedAtUtc = DateTimeOffset.UtcNow;
            }

            var session = new Models.FishingSession();
            db.FishingSessions.Add(session);
            await db.SaveChangesAsync(cancellationToken);
            _activeSessionId = session.Id;
            logger.LogInformation("New fishing session {SessionId} started.", session.Id);
            return session;
        }
        finally { _gate.Release(); }
    }
    
    public async Task<Models.FishingSession> EndAndProcessAsync(CancellationToken cancellationToken = default)
{
    await _gate.WaitAsync(cancellationToken);
    try
    {
        if (_activeSessionId is not { } sessionId) throw new InvalidOperationException("There is no active fishing session.");
        var capturePath = await screenCaptureService.CaptureVirtualScreenAsync(cancellationToken);
        var profiles = await roiProfiles.GetAllAsync(cancellationToken);
        var catchOcr = ReadCatchFromScreen(capturePath, profiles);

        var rarityRegion = profiles
            .FirstOrDefault(profile => string.Equals(profile.Name, "Catch rarity", StringComparison.OrdinalIgnoreCase))
            ?.Region;
        var detectedRarity = rarityRegion is null
            ? null
            : screenCaptureService.DetectTagRarity(capturePath, rarityRegion);

        var ocr = catchOcr.Result;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var session = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
            db.FishingSessions, s => s.Id == sessionId, cancellationToken);
        session.EndedAtUtc = DateTimeOffset.UtcNow;
        session.CapturePath = capturePath;
        session.RawOcrText = ocr.Text;
        session.OcrConfidence = ocr.Confidence;

        var parsedCatch = catchOcr.IsSplit
            ? catchOcrParser.ParseFields(catchOcr.SpeciesText!, catchOcr.DetailsText!, detectedRarity)
            : catchOcrParser.Parse(ocr.Text, detectedRarity);

        if (parsedCatch.HasCatchDetails)
        {
            var fishImageProfile = profiles
                .FirstOrDefault(item => string.Equals(item.Name, "Fish image", StringComparison.OrdinalIgnoreCase));
            db.Catches.Add(new Models.Catch
            {
                FishingSessionId = session.Id,
                Species = parsedCatch.Species!,
                WeightKg = parsedCatch.WeightKg,
                LengthCm = parsedCatch.LengthCm,
                Rarity = parsedCatch.Rarity,
                ImagePath = fishImageProfile is null
                    ? null
                    : await screenCaptureService.PersistCatchImageAsync(capturePath, fishImageProfile.Region, cancellationToken),
                RawOcrText = ocr.Text
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        _activeSessionId = null;
        logger.LogInformation("Fishing session {SessionId} ended with OCR confidence {Confidence:P0}.", sessionId, ocr.Confidence);
        return session;
    }
    finally { _gate.Release(); }
}

    /// <summary>
    /// Captures the current screen without requiring the user to start a session first.
    /// If a session is active, this keeps the original start/end workflow; otherwise a
    /// completed one-off record is created solely to persist the OCR result.
    /// </summary>
    public async Task<Models.FishingSession> CaptureAndProcessAsync(CancellationToken cancellationToken = default)
    {
        if (ActiveSessionId is null)
            await StartAsync(cancellationToken);

        return await EndAndProcessAsync(cancellationToken);
    }

    private CatchCardOcr ReadCatchFromScreen(string capturePath, IReadOnlyList<RoiProfile> profiles)
    {
        var species = profiles.FirstOrDefault(p =>
                          string.Equals(p.Name, "Catch species", StringComparison.OrdinalIgnoreCase))
                      ?? throw new InvalidOperationException("A 'Catch species' ROI profile is required.");
        var info = profiles.FirstOrDefault(p =>
                       string.Equals(p.Name, "Catch info", StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException("A 'Catch info' ROI profile is required.");

        var templatesDir = Path.Combine(environment.ContentRootPath, "data", "templates");
        var bagIcon = screenCaptureService.FindIcon(capturePath, info.Region, Path.Combine(templatesDir, "bag.png"));
        var rulerIcon = screenCaptureService.FindIcon(capturePath, info.Region, Path.Combine(templatesDir, "ruler.png"));

        OcrRegion weightRegion, lengthRegion;
        if (bagIcon is { } bag && rulerIcon is { } ruler)
        {
            const double weightOffsetX = 0.006;
            const double lengthOffsetX = 0.006;
            const double regionW = 0.065;
            const double regionH = 0.030;
            const double regionOffsetY = 0.010;

            weightRegion = new OcrRegion(bag.X + weightOffsetX, bag.Y - regionOffsetY, regionW, regionH);
            lengthRegion = new OcrRegion(ruler.X + lengthOffsetX, ruler.Y - regionOffsetY, regionW, regionH);
        }
        else
        {
            throw new InvalidOperationException("Icon detection failed. Check that data/templates/bag.png and ruler.png exist and match the current game UI.");
        }

        logger.LogInformation(
            "Icon detect — bag={Bag}, ruler={Ruler}, weight x={WX:F3} y={WY:F3}, length x={LX:F3} y={LY:F3}",
            bagIcon is not null, rulerIcon is not null,
            weightRegion.X, weightRegion.Y, lengthRegion.X, lengthRegion.Y);

        return ocrService.ReadRefinedCatch(capturePath, species.Region, weightRegion, lengthRegion);
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
            : screenCaptureService.DetectTagRarity(capturePath, rarityProfile.Region);
        var parsedCatch = catchOcr.IsSplit
            ? catchOcrParser.ParseFields(catchOcr.SpeciesText!, catchOcr.DetailsText!, detectedRarity)
            : catchOcrParser.Parse(catchOcr.Result.Text, detectedRarity);
        var ocr = catchOcr.Result;
        if (!parsedCatch.HasCatchDetails) return false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            Models.FishingSession session;
            if (_activeSessionId is { } sessionId)
            {
                session = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
                    db.FishingSessions, item => item.Id == sessionId, cancellationToken);
            }
            else
            {
                session = new Models.FishingSession();
                db.FishingSessions.Add(session);
            }

            var fishImageProfile = (await roiProfiles.GetAllAsync(cancellationToken))
                .FirstOrDefault(item => string.Equals(item.Name, "Fish image", StringComparison.OrdinalIgnoreCase));
            var imagePath = fishImageProfile is null
                ? null
                : await screenCaptureService.PersistCatchImageAsync(capturePath, fishImageProfile.Region, cancellationToken);

            session.Catches.Add(new Models.Catch
            {
                Species = parsedCatch.Species!,
                WeightKg = parsedCatch.WeightKg,
                LengthCm = parsedCatch.LengthCm,
                Rarity = parsedCatch.Rarity,
                ImagePath = imagePath,
                RawOcrText = ocr.Text,
                FishingSession = session
            });
            session.CapturePath = capturePath;
            session.RawOcrText = ocr.Text;
            session.OcrConfidence = ocr.Confidence;
            await db.SaveChangesAsync(cancellationToken);
            _activeSessionId = session.Id;
            logger.LogInformation("Automatically detected catch {Species} ({WeightKg} kg) in session {SessionId}.",
                parsedCatch.Species, parsedCatch.WeightKg, session.Id);
            return true;
        }
        finally { _gate.Release(); }
    }
}
