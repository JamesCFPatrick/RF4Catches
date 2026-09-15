namespace RF4Catches.Features.Captures;

public sealed class CaptureRetentionOptions
{
    public bool Enabled { get; init; } = true;
    public int IntervalMinutes { get; init; } = 60;

    /// <summary>Captures older than this are deleted unless a pending review references them.</summary>
    public int CaptureMaxAgeHours { get; init; } = 24;

    /// <summary>If true, also prune old files in data/ocr-samples.</summary>
    public bool CleanOcrSamples { get; init; } = false;
    public int OcrSampleMaxAgeDays { get; init; } = 30;
}