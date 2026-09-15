using Microsoft.EntityFrameworkCore;
using RF4Catches.Data;
using RF4Catches.Endpoints;
using RF4Catches.HostedServices;
using RF4Catches.Services;

var builder = WebApplication.CreateBuilder(args);

// Services
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

// Middleware
app.UseDefaultFiles();
app.UseStaticFiles();
// app.UseAntiforgery(); Will keep in case auth is req in the future.

// Database startup / migrations / cleanup
await using (var db = await app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
{
    await db.Database.EnsureCreatedAsync();
    await EnsureCatchSchemaAsync(db);
    await BackfillCatchFieldsAsync(db, app.Services.GetRequiredService<CatchOcrParser>());

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

// Map all endpoint groups
app.MapSessionEndpoints();
app.MapDashboardEndpoints();
app.MapHistoryEndpoints();
app.MapCaptureEndpoints();
app.MapRoiEndpoints();

app.Run();

// ---------- Helper methods (keep these here for now) ----------

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