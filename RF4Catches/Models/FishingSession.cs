namespace RF4Catches.Models;

public class FishingSession
{
    public int Id { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAtUtc { get; set; }
    public string? CapturePath { get; set; }
    public string? RawOcrText { get; set; }
    public float? OcrConfidence { get; set; }
    public decimal? MarketTotal { get; set; }
    public string? FishingMethod { get; set; }
    public string? Baits { get; set; }
    public string? LineClip { get; set; }
    public decimal? HookDepthCm { get; set; }
    public string? MapName { get; set; }
    public string? MapCoordinates { get; set; }
    public decimal? CafeSilver { get; set; }
    public decimal? MarketSilver { get; set; }
    public List<Catch> Catches { get; set; } = [];
}
