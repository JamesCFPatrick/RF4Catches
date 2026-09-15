using Microsoft.EntityFrameworkCore;
using RF4Catches.Data;
using RF4Catches.HostedServices;
using RF4Catches.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddDbContextFactory<AppDbContext>(opt =>
    opt.UseSqlite("Data Source=catches.db"));
builder.Services.Configure<OcrOptions>(builder.Configuration.GetSection("Ocr"));
builder.Services.Configure<HotkeyOptions>(builder.Configuration.GetSection("Hotkeys"));
builder.Services.Configure<AutomaticDetectionOptions>(builder.Configuration.GetSection("AutomaticDetection"));
builder.Services.AddSingleton<ScreenCaptureService>();
builder.Services.AddSingleton<OcrService>();
builder.Services.AddSingleton<RoiProfileStore>();
builder.Services.AddSingleton<FishNameMatcher>();
builder.Services.AddSingleton<CatchOcrParser>();
builder.Services.AddSingleton<SessionService>();
builder.Services.AddHostedService<AutomaticDetectionService>();
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(5);
});


var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAntiforgery();

await using (var db = await app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
{
    await db.Database.EnsureCreatedAsync();
    await EnsureCatchSchemaAsync(db);
    await BackfillCatchFieldsAsync(
        db,
        app.Services.GetRequiredService<CatchOcrParser>());
    var pendingCapturePaths = (await db.PendingReviews
        .Where(review => review.CapturePath != null)
        .Select(review => review.CapturePath!)
        .ToListAsync())
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    app.Services.GetRequiredService<ScreenCaptureService>().DeletePersistedCaptures(pendingCapturePaths);
    var sessionsWithCaptures = await db.FishingSessions
        .Where(session => session.CapturePath != null)
        .ToListAsync();
    if (sessionsWithCaptures.Count > 0)
    {
        foreach (var session in sessionsWithCaptures)
            session.CapturePath = null;
        await db.SaveChangesAsync();
    }
}

app.MapGet("/api/session", (SessionService sessions) => Results.Ok(new { activeSessionId = sessions.ActiveSessionId }));
app.MapGet("/api/pending-reviews", async (
    IDbContextFactory<AppDbContext> dbContextFactory,
    CancellationToken cancellationToken) =>
{
    await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
    var reviews = await db.PendingReviews
        .OrderByDescending(review => review.Id)
        .Take(100)
        .Select(review => new
        {
            review.Id,
            review.CreatedAtUtc,
            review.OcrConfidence,
            review.Reason,
            review.RawOcrText,
            review.CapturePath
        })
        .ToListAsync(cancellationToken);
    return Results.Ok(reviews);
});
app.MapGet("/api/sessions/latest", async Task<IResult> (IDbContextFactory<AppDbContext> dbContextFactory, CatchOcrParser catchOcrParser, CancellationToken cancellationToken) =>
{
    await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
    var session = await db.FishingSessions
        .OrderByDescending(s => s.Id)
        .Select(s => new
        {
            s.Id,
            s.StartedAtUtc,
            s.EndedAtUtc,
            s.RawOcrText,
            s.OcrConfidence,
            s.CapturePath
        })
        .FirstOrDefaultAsync(cancellationToken);
    if (session is null) return Results.NotFound();

    var parsedCatch = catchOcrParser.Parse(session.RawOcrText ?? "");
    return Results.Ok(new { session, parsedCatch });
});
app.MapGet("/dashboard/content", async (
    IDbContextFactory<AppDbContext> dbContextFactory,
    SessionService sessionService,
    CancellationToken cancellationToken) =>
{
    await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
    var today = DateTimeOffset.UtcNow.Date;
    var activeSessionCatches = sessionService.ActiveSessionId is { } activeSessionId
        ? await db.Catches
            .Where(c => c.FishingSessionId == activeSessionId)
            .Select(c => new DashboardCatch(c.Species, c.WeightKg, c.LengthCm, c.Rarity, c.ImagePath, c.CaughtAtUtc, c.FishingSessionId))
            .ToListAsync(cancellationToken)
        : [];
    var activeSessionDetails = sessionService.ActiveSessionId is { } currentSessionId
        ? await db.FishingSessions
            .Where(session => session.Id == currentSessionId)
            .Select(session => new SessionDetails(session.FishingMethod, session.Baits, session.LineClip, session.HookDepthCm, session.MapName, session.MapCoordinates, session.CafeSilver, session.MarketSilver))
            .FirstOrDefaultAsync(cancellationToken)
        : null;
    activeSessionCatches = activeSessionCatches
        .Select(catchRecord => catchRecord with
        {
            WeightKg = catchRecord.WeightKg is { } weightKg
                ? CatchWeight.NormalizeKg(weightKg)
                : null
        })
        .ToList();
    var totalCatches = activeSessionCatches.Count;
    var totalWeightKg = activeSessionCatches.Sum(catchRecord => catchRecord.WeightKg ?? 0m);
    var averageWeightKg = totalCatches == 0 ? 0m : totalWeightKg / totalCatches;
    var catchesToday = activeSessionCatches.Count(catchRecord => catchRecord.CaughtAtUtc >= today);
    var recentCatches = activeSessionCatches
        .OrderByDescending(c => c.CaughtAtUtc)
        .Take(25)
        .ToList();

    return Results.Content(
        DashboardMarkup.RenderContent(new DashboardData(
            totalCatches,
            totalWeightKg,
            averageWeightKg,
            catchesToday,
            sessionService.ActiveSessionId,
            activeSessionDetails,
            recentCatches)),
        "text/html");
});

app.MapGet("/dashboard/session", async (
    IDbContextFactory<AppDbContext> dbContextFactory,
    SessionService sessionService,
    CancellationToken cancellationToken) =>
{
    SessionDetails? details = null;
    if (sessionService.ActiveSessionId is { } activeSessionId)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        details = await db.FishingSessions
            .Where(s => s.Id == activeSessionId)
            .Select(s => new SessionDetails(
                s.FishingMethod,
                s.Baits,
                s.LineClip,
                s.HookDepthCm,
                s.MapName,
                s.MapCoordinates,
                s.CafeSilver,
                s.MarketSilver))
            .FirstOrDefaultAsync(cancellationToken);
    }

    var data = new DashboardData(
        0, 0m, 0m, 0,
        sessionService.ActiveSessionId,
        details,
        Array.Empty<DashboardCatch>());

    return Results.Content(DashboardMarkup.RenderSession(data), "text/html");
});

app.MapGet("/history/content", async (
    IDbContextFactory<AppDbContext> dbContextFactory,
    CancellationToken cancellationToken) =>
{
    await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
    var sessions = await db.FishingSessions
        .Select(session => new HistorySession(
            session.Id,
            session.StartedAtUtc,
            session.EndedAtUtc,
            new SessionDetails(session.FishingMethod, session.Baits, session.LineClip, session.HookDepthCm, session.MapName, session.MapCoordinates, session.CafeSilver, session.MarketSilver),
            Array.Empty<HistoryCatch>()))
        .ToListAsync(cancellationToken);
    var catches = await db.Catches
        .Select(catchRecord => new
        {
            catchRecord.FishingSessionId,
            Catch = new HistoryCatch(catchRecord.Species, catchRecord.WeightKg, catchRecord.LengthCm, catchRecord.Rarity, catchRecord.ImagePath, catchRecord.CaughtAtUtc)
        })
        .ToListAsync(cancellationToken);
    var catchesBySession = catches
        .GroupBy(item => item.FishingSessionId)
        .ToDictionary(group => group.Key, group => (IReadOnlyList<HistoryCatch>)group.Select(item => item.Catch).ToList());
    var history = sessions
        .OrderByDescending(session => session.Id)
        .Select(session => session with
        {
            Catches = catchesBySession.TryGetValue(session.Id, out var sessionCatches)
                ? sessionCatches
                : Array.Empty<HistoryCatch>()
        })
        .Where(session => session.Catches.Count > 0)
        .ToList();

    return Results.Content(HistoryMarkup.Render(history), "text/html");
});
app.MapGet("/history", () => Results.Redirect("/history.html"));
app.MapPost("/dashboard/session/details", async Task<IResult> (
    [Microsoft.AspNetCore.Mvc.FromForm] SessionDetailsForm form,
    SessionService sessionService,
    CancellationToken cancellationToken) =>
{
    await sessionService.UpdateDetailsAsync(form.ToDetails(), cancellationToken);
    return Results.NoContent();
});
app.MapGet("/api/catch-image/{fileName}", (string fileName, IWebHostEnvironment environment) =>
{
    if (!string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) ||
        !fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest();

    var path = Path.Combine(environment.ContentRootPath, "data", "catch-images", fileName);
    return File.Exists(path) ? Results.File(path, "image/jpeg") : Results.NotFound();
});
app.MapPost("/dashboard/session/new", async (
    SessionService sessionService,
    HttpResponse response,
    CancellationToken cancellationToken) =>
{
    await sessionService.StartNewAsync(cancellationToken);
    response.Headers.Append("HX-Trigger", "sessionChanged");
    return Results.NoContent();
});
app.MapPost("/dashboard/session/end", async (
    SessionService sessionService,
    HttpResponse response,
    CancellationToken cancellationToken) =>
{
    await sessionService.EndAndProcessAsync(cancellationToken);
    response.Headers.Append("HX-Trigger", "sessionChanged");
    return Results.NoContent();
});
app.MapGet("/dashboard", () => Results.Redirect("/"));
app.MapPost("/api/session/start", async (SessionService sessions, CancellationToken cancellationToken) =>
    Results.Ok(await sessions.StartAsync(cancellationToken)));
app.MapPost("/api/session/end", async (SessionService sessions, CancellationToken cancellationToken) =>
    Results.Ok(await sessions.EndAndProcessAsync(cancellationToken)));
app.MapPost("/api/capture", async (SessionService sessions, CancellationToken cancellationToken) =>
    Results.Ok(await sessions.CaptureAndProcessAsync(cancellationToken)));
app.MapPost("/api/capture/test", async (SessionService sessions, CancellationToken cancellationToken) =>
{
    try
    {
        var result = await sessions.TestCaptureAsync(cancellationToken);
        return Results.Content(DashboardMarkup.RenderCaptureTest(result), "text/html");
    }
    catch (Exception ex)
    {
        return Results.Content(
            $"<div class='alert alert-error'><pre>{System.Net.WebUtility.HtmlEncode(ex.ToString())}</pre></div>",
            "text/html");
    }
});

app.MapPost("/api/ocr", async Task<IResult> (IFormFile image, OcrService ocr, CatchOcrParser catchOcrParser, IWebHostEnvironment environment, CancellationToken cancellationToken) =>
{
    const long maxImageBytes = 20 * 1024 * 1024;
    var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" };
    var extension = Path.GetExtension(image.FileName);

    if (image.Length == 0 || image.Length > maxImageBytes || !allowedExtensions.Contains(extension))
        return Results.BadRequest(new { error = "Upload a PNG, JPEG, BMP, or TIFF image smaller than 20 MB." });

    var samplesDirectory = Path.Combine(environment.ContentRootPath, "data", "ocr-samples");
    Directory.CreateDirectory(samplesDirectory);
    var savedFileName = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}{extension}";
    var savedPath = Path.Combine(samplesDirectory, savedFileName);

    await using (var stream = File.Create(savedPath))
        await image.CopyToAsync(stream, cancellationToken);

    try
    {
        var result = ocr.Read(savedPath);
        return Results.Ok(new
        {
            image = Path.Combine("data", "ocr-samples", savedFileName),
            result.Text,
            result.Confidence,
            parsedCatch = catchOcrParser.Parse(result.Text)
        });
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
}).DisableAntiforgery();

app.MapGet("/roi", () => Results.Redirect("/roi.html"));
app.MapGet("/roi/antiforgery-token", (Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery, HttpContext context) =>
    Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken }));

app.MapPost("/roi/upload", async Task<IResult> (IFormFile image, IWebHostEnvironment environment, CancellationToken cancellationToken) =>
{
    var extension = Path.GetExtension(image.FileName);
    var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" };
    if (image.Length == 0 || image.Length > 20 * 1024 * 1024 || !allowedExtensions.Contains(extension))
        return Results.BadRequest("Upload a PNG, JPEG, BMP, or TIFF image smaller than 20 MB.");

    var directory = Path.Combine(environment.ContentRootPath, "data", "ocr-samples");
    Directory.CreateDirectory(directory);
    var fileName = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}{extension}";
    await using var stream = File.Create(Path.Combine(directory, fileName));
    await image.CopyToAsync(stream, cancellationToken);
    return Results.Content(RoiEditorMarkup.Editor(fileName), "text/html");
}).DisableAntiforgery();

app.MapGet("/roi/image/{fileName}", (string fileName, IWebHostEnvironment environment) =>
{
    if (!string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal)) return Results.BadRequest();
    var path = Path.Combine(environment.ContentRootPath, "data", "ocr-samples", fileName);
    return File.Exists(path) ? Results.File(path) : Results.NotFound();
});

app.MapPost("/roi/test", ([Microsoft.AspNetCore.Mvc.FromForm] RoiForm form, OcrService ocr, CatchOcrParser catchOcrParser, ScreenCaptureService screenCapture, IWebHostEnvironment environment) =>
{
    var path = Path.Combine(environment.ContentRootPath, "data", "ocr-samples", Path.GetFileName(form.Image));
    if (!File.Exists(path)) return Results.Content(RoiEditorMarkup.Error("The uploaded image no longer exists."), "text/html");
    try
    {
        var result = ocr.ReadRegion(path, form.ToRegion());
        var detectedRarity = screenCapture.DetectTagRarity(path, form.ToRegion());
        return Results.Content(RoiEditorMarkup.Result(result, catchOcrParser.Parse(result.Text, detectedRarity)), "text/html");
    }
    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
    {
        return Results.Content(RoiEditorMarkup.Error(exception.Message), "text/html");
    }
});

app.MapPost("/roi/save", async Task<IResult> ([Microsoft.AspNetCore.Mvc.FromForm] RoiForm form, RoiProfileStore profiles, CancellationToken cancellationToken) =>
{
    try
    {
        await profiles.SaveAsync(form.Name, form.ToRegion(), cancellationToken);
        return Results.Content(RoiEditorMarkup.Profiles(await profiles.GetAllAsync(cancellationToken)), "text/html");
    }
    catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException)
    {
        return Results.Content(RoiEditorMarkup.Error(exception.Message), "text/html");
    }
});

app.MapPost("/roi/delete", async Task<IResult> (
    [Microsoft.AspNetCore.Mvc.FromForm] RoiDeleteForm form,
    RoiProfileStore profiles,
    CancellationToken cancellationToken) =>
{
    await profiles.DeleteAsync(form.Name, cancellationToken);
    return Results.Content(RoiEditorMarkup.Profiles(await profiles.GetAllAsync(cancellationToken)), "text/html");
});

app.MapGet("/roi/profiles", async (RoiProfileStore profiles, CancellationToken cancellationToken) =>
    Results.Content(RoiEditorMarkup.Profiles(await profiles.GetAllAsync(cancellationToken)), "text/html"));

app.Run();

static async Task EnsureCatchSchemaAsync(AppDbContext db)
{
    var connection = db.Database.GetDbConnection();
    if (connection.State != System.Data.ConnectionState.Open)
        await connection.OpenAsync();

    await EnsureColumnsAsync(connection, "Catches",
    [
        ("LengthCm", "REAL NULL"),
        ("Rarity", "TEXT NULL"),
        ("ImagePath", "TEXT NULL")
    ]);
    await EnsureColumnsAsync(connection, "FishingSessions",
    [
        ("FishingMethod", "TEXT NULL"),
        ("Baits", "TEXT NULL"),
        ("LineClip", "TEXT NULL"),
        ("HookDepthCm", "REAL NULL"),
        ("MapName", "TEXT NULL"),
        ("MapCoordinates", "TEXT NULL"),
        ("CafeSilver", "REAL NULL"),
        ("MarketSilver", "REAL NULL")
    ]);

    await using var pendingTable = connection.CreateCommand();
    pendingTable.CommandText = """
        CREATE TABLE IF NOT EXISTS PendingReviews (
            Id INTEGER NOT NULL CONSTRAINT PK_PendingReviews PRIMARY KEY AUTOINCREMENT,
            CreatedAtUtc TEXT NOT NULL,
            RawOcrText TEXT NOT NULL,
            OcrConfidence REAL NOT NULL,
            CapturePath TEXT NULL,
            Reason TEXT NOT NULL
        );
        """;
    await pendingTable.ExecuteNonQueryAsync();
}

static async Task EnsureColumnsAsync(
    System.Data.Common.DbConnection connection,
    string tableName,
    IReadOnlyList<(string Name, string Definition)> definitions)
{
    await using var command = connection.CreateCommand();
    command.CommandText = $"PRAGMA table_info({tableName});";
    var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(1));
    }

    foreach (var (name, definition) in definitions)
    {
        if (columns.Contains(name)) continue;
        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {name} {definition};";
        await alter.ExecuteNonQueryAsync();
    }
}

static async Task BackfillCatchFieldsAsync(AppDbContext db, CatchOcrParser parser)
{
    var catches = await db.Catches
        .Where(catchRecord => catchRecord.RawOcrText != null &&
                              (catchRecord.Rarity == null || catchRecord.LengthCm == null))
        .ToListAsync();

    foreach (var catchRecord in catches)
    {
        var parsed = parser.Parse(catchRecord.RawOcrText!);
        catchRecord.Rarity ??= parsed.Rarity;
        catchRecord.LengthCm ??= parsed.LengthCm;
    }

    if (catches.Count > 0)
        await db.SaveChangesAsync();
}

public sealed class RoiForm
{
    public string Name { get; init; } = "Catch card";
    public string Image { get; init; } = "";
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public OcrRegion ToRegion() => new(X, Y, Width, Height);
}

public sealed class RoiDeleteForm
{
    public string Name { get; init; } = "";
}

public sealed class SessionDetailsForm
{
    public string? FishingMethod { get; init; }
    public string? Baits { get; init; }
    public string? LineClip { get; init; }
    public decimal? HookDepthCm { get; init; }
    public string? MapName { get; init; }
    public string? MapCoordinates { get; init; }
    public decimal? CafeSilver { get; init; }
    public decimal? MarketSilver { get; init; }

    public SessionDetails ToDetails() => new(
        Normalize(FishingMethod),
        Normalize(Baits),
        Normalize(LineClip),
        HookDepthCm,
        Normalize(MapName),
        Normalize(MapCoordinates),
        CafeSilver,
        MarketSilver);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
