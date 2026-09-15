namespace RF4Catches.Models;

public class PendingReview
{
    public int Id { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string RawOcrText { get; set; } = "";
    public float OcrConfidence { get; set; }
    public string? CapturePath { get; set; }
    public string Reason { get; set; } = "";
}
