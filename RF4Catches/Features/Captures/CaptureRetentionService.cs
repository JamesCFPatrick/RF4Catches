using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RF4Catches.Data;
using RF4Catches.Services;

namespace RF4Catches.Features.Captures;

public sealed class CaptureRetentionService(
    IServiceScopeFactory scopeFactory,
    IWebHostEnvironment environment,
    IOptions<CaptureRetentionOptions> options,
    ILogger<CaptureRetentionService> logger) : BackgroundService
{
    private readonly CaptureRetentionOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;

        // Let the DB initializer finish first.
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.IntervalMinutes));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Capture retention pass failed."); }

            try { await timer.WaitForNextTickAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task<CleanupStats> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var preserved = (await db.PendingReviews
                .Where(r => r.CapturePath != null)
                .Select(r => r.CapturePath!)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var (capturesDeleted, bytesFreed) = PruneDirectory(
            Path.Combine(environment.ContentRootPath, "data", "captures"),
            "catch-*.png",
            DateTime.UtcNow.AddHours(-_options.CaptureMaxAgeHours),
            preserved);

        var samplesDeleted = 0;
        if (_options.CleanOcrSamples)
        {
            (samplesDeleted, _) = PruneDirectory(
                Path.Combine(environment.ContentRootPath, "data", "ocr-samples"),
                "*",
                DateTime.UtcNow.AddDays(-_options.OcrSampleMaxAgeDays),
                preservedPaths: null);
        }

        if (capturesDeleted > 0 || samplesDeleted > 0)
            logger.LogInformation(
                "Retention: deleted {Captures} captures ({MB:F2} MB) and {Samples} OCR samples.",
                capturesDeleted, bytesFreed / 1024.0 / 1024.0, samplesDeleted);

        return new CleanupStats(capturesDeleted, bytesFreed, samplesDeleted);
    }

    private (int Deleted, long Bytes) PruneDirectory(
        string directory,
        string pattern,
        DateTime cutoffUtc,
        IReadOnlySet<string>? preservedPaths)
    {
        if (!Directory.Exists(directory)) return (0, 0);

        var deleted = 0;
        var bytes = 0L;
        foreach (var path in Directory.EnumerateFiles(directory, pattern))
        {
            if (preservedPaths?.Contains(path) == true) continue;

            var info = new FileInfo(path);
            if (info.LastWriteTimeUtc > cutoffUtc) continue;

            try
            {
                bytes += info.Length;
                File.Delete(path);
                deleted++;
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Could not delete {Path}", path);
            }
        }

        return (deleted, bytes);
    }
    
    /// <summary>
    /// Best-effort delete of a temporary capture. Never throws;
    /// the retention service will pick up leftovers if this fails.
    /// </summary>
    public void TryDeleteCapture(string capturePath)
    {
        if (string.IsNullOrWhiteSpace(capturePath)) return;
        try
        {
            if (File.Exists(capturePath)) File.Delete(capturePath);
        }
        catch (IOException) { /* locked or in use — retention will clean later */ }
        catch (UnauthorizedAccessException) { /* same */ }
    }

    public sealed record CleanupStats(int CapturesDeleted, long BytesFreed, int SamplesDeleted);
}