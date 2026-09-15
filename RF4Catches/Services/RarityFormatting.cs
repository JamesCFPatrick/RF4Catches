namespace RF4Catches.Services;

public static class RarityFormatting
{
    public static string? ToDisplayString(this IReadOnlyList<string>? rarities) =>
        rarities is null || rarities.Count == 0 ? null : string.Join(", ", rarities);
}