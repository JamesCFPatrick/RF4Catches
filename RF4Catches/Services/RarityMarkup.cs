using System.Net;

namespace RF4Catches.Services;

public static class RarityMarkup
{
    public static string RenderBadges(string? rarity)
    {
        if (string.IsNullOrWhiteSpace(rarity)) return "—";
        var tags = SplitTags(rarity);
        return tags.Length == 0 ? "—" : string.Join(" ", tags.Select(RenderSingleBadge));
    }

    public static string CardClass(string? rarity)
    {
        if (string.IsNullOrWhiteSpace(rarity)) return "";
        return SplitTags(rarity).Any(IsPrism) ? "prism-card" : "";
    }

    private static string[] SplitTags(string rarity) =>
        rarity.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsPrism(string tag) =>
        tag.Equals("Rare", StringComparison.OrdinalIgnoreCase) ||
        tag.Equals("Trophy", StringComparison.OrdinalIgnoreCase) ||
        tag.Equals("Rare Trophy", StringComparison.OrdinalIgnoreCase);

    private static string RenderSingleBadge(string tag)
    {
        var encoded = WebUtility.HtmlEncode(tag);
        var style = tag.Equals("Valuable", StringComparison.OrdinalIgnoreCase) ? "badge-success"
            : tag.Equals("Rare Trophy", StringComparison.OrdinalIgnoreCase) ? "badge-info"
            : tag.Equals("Trophy", StringComparison.OrdinalIgnoreCase) ? "badge-warning"
            : tag.Equals("Rare", StringComparison.OrdinalIgnoreCase) ? "badge-secondary"
            : "badge-ghost";
        return $"""<span class="badge {style}">{encoded}</span>""";
    }
}