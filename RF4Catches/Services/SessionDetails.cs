namespace RF4Catches.Services;

/// <summary>
/// User-entered context attached to one fishing session. Baits and coordinates
/// remain text so users can record multiple bait types and game-specific formats.
/// </summary>
public sealed record SessionDetails(
    string? FishingMethod,
    string? Baits,
    string? LineClip,
    decimal? HookDepthCm,
    string? MapName,
    string? MapCoordinates,
    decimal? CafeSilver,
    decimal? MarketSilver);
