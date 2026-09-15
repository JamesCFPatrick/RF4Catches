using Microsoft.EntityFrameworkCore;
using RF4Catches.Services;

namespace RF4Catches.Endpoints;

public static class CaptureEndpoints
{
    public static void MapCaptureEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/capture", async (SessionService sessions, CancellationToken ct) =>
            Results.Ok(await sessions.CaptureAndProcessAsync(ct)));

        app.MapPost("/api/capture/test", async (SessionService sessions, CancellationToken ct) =>
        {
            try
            {
                var result = await sessions.TestCaptureAsync(ct);
                return Results.Content(DashboardMarkup.RenderCaptureTest(result), "text/html");
            }
            catch (Exception ex)
            {
                return Results.Content(
                    $"<div class='alert alert-error'><pre>{System.Net.WebUtility.HtmlEncode(ex.ToString())}</pre></div>",
                    "text/html");
            }
        });

        app.MapPost("/api/ocr", async Task<IResult> (
            IFormFile image,
            OcrService ocr,
            CatchOcrParser catchOcrParser,
            IWebHostEnvironment environment,
            CancellationToken ct) =>
        {
            const long maxImageBytes = 20 * 1024 * 1024;
            var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" };

            var extension = Path.GetExtension(image.FileName);
            if (image.Length == 0 || image.Length > maxImageBytes || !allowedExtensions.Contains(extension))
                return Results.BadRequest(new { error = "Upload a PNG, JPEG, BMP, or TIFF image smaller than 20 MB." });

            var samplesDirectory = Path.Combine(environment.ContentRootPath, "data", "ocr-samples");
            Directory.CreateDirectory(samplesDirectory);
            var savedFileName = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}{extension}";
            var savedPath = Path.Combine(samplesDirectory, savedFileName);

            await using (var stream = File.Create(savedPath))
                await image.CopyToAsync(stream, ct);

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

        app.MapGet("/api/pending-reviews", async (
            IDbContextFactory<Data.AppDbContext> dbContextFactory,
            CancellationToken ct) =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(ct);
            var reviews = await db.PendingReviews
                .OrderByDescending(r => r.Id)
                .Take(100)
                .Select(r => new
                {
                    r.Id, r.CreatedAtUtc, r.OcrConfidence, r.Reason, r.RawOcrText, r.CapturePath
                })
                .ToListAsync(ct);
            return Results.Ok(reviews);
        });

        app.MapGet("/api/sessions/latest", async Task<IResult> (
            IDbContextFactory<Data.AppDbContext> dbContextFactory,
            CatchOcrParser catchOcrParser,
            CancellationToken ct) =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(ct);
            var session = await db.FishingSessions
                .OrderByDescending(s => s.Id)
                .Select(s => new { s.Id, s.StartedAtUtc, s.EndedAtUtc, s.RawOcrText, s.OcrConfidence, s.CapturePath })
                .FirstOrDefaultAsync(ct);

            if (session is null) return Results.NotFound();

            var parsedCatch = catchOcrParser.Parse(session.RawOcrText ?? "");
            return Results.Ok(new { session, parsedCatch });
        });

        app.MapGet("/api/catch-image/{fileName}", (string fileName, IWebHostEnvironment environment) =>
        {
            if (!string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) ||
                !fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest();

            var path = Path.Combine(environment.ContentRootPath, "data", "catch-images", fileName);
            return File.Exists(path) ? Results.File(path, "image/jpeg") : Results.NotFound();
        });
        
        app.MapDelete("/api/catch/{id:int}", async Task<IResult> (
            int id,
            HttpContext context,
            IDbContextFactory<Data.AppDbContext> dbContextFactory,
            SessionService sessionService,
            CancellationToken ct) =>
        {
            if (!await sessionService.DeleteCatchAsync(id, ct))
            {
                context.Response.Headers.Append("HX-Trigger",
                    """{"showToast":{"message":"Catch could not be found","type":"error"}}""");
                return Results.NotFound();
            }

            context.Response.Headers.Append("HX-Trigger",
                """{"showToast":{"message":"Catch deleted","type":"success"}}""");

            // The dashboard renders summary stats above the catch grid; those go
            // stale when a catch disappears. Emit an out-of-band swap so they
            // refresh in place. History has no such target, so skip it there.
            var currentUrl = context.Request.Headers["HX-Current-URL"].ToString();
            var isDashboard = currentUrl.EndsWith("/", StringComparison.Ordinal)
                              || currentUrl.Contains("/dashboard", StringComparison.Ordinal)
                              || currentUrl.Contains("index.html", StringComparison.OrdinalIgnoreCase);

            if (!isDashboard)
                return Results.Ok();

            var data = await DashboardEndpoints.BuildDashboardDataAsync(dbContextFactory, sessionService, ct);
            var oob = DashboardMarkup.RenderStats(data).Replace(
                "<div id=\"dashboard-stats\"",
                "<div id=\"dashboard-stats\" hx-swap-oob=\"true\"",
                StringComparison.Ordinal);

            return Results.Content(oob, "text/html");
        });

        app.MapPost("/api/catch/{id:int}/restore", async (int id, SessionService sessionService, CancellationToken ct) =>
        {
            var ok = await sessionService.RestoreCatchAsync(id, ct);
            return ok ? Results.Ok() : Results.NotFound();
        });
    }
}

