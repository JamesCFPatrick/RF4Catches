namespace RF4Catches.Models;

public class Catch
{
    public int Id { get; set; }
    public int FishingSessionId { get; set; }
    public FishingSession? FishingSession { get; set; }
    public required string Species { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? LengthCm { get; set; }
    public string? Rarity { get; set; }
    public string? ImagePath { get; set; }
    public decimal? SaleValue { get; set; }
    public DateTimeOffset CaughtAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? RawOcrText { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
}
