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

        var tags = SplitTags(rarity);
        var classes = new List<string>(3);

        if (tags.Any(t => Matches(t, "Rare Trophy"))) classes.Add("card-rare-trophy");
        else if (tags.Any(t => Matches(t, "Rare"))) classes.Add("card-rare");
        else if (tags.Any(t => Matches(t, "Trophy"))) classes.Add("card-trophy");

        if (tags.Any(t => Matches(t, "Valuable"))) classes.Add("card-valuable");

        return string.Join(" ", classes);
    }

    private static string[] SplitTags(string rarity) =>
        rarity.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool Matches(string tag, string expected) =>
        tag.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static string RenderSingleBadge(string tag)
    {
        var encoded = WebUtility.HtmlEncode(tag);
        var style = Matches(tag, "Valuable") ? "badge-success"
            : Matches(tag, "Rare Trophy") ? "badge-info"
            : Matches(tag, "Trophy") ? "badge-warning"
            : Matches(tag, "Rare") ? "badge-secondary"
            : "badge-ghost";
        return $"""<span class="badge {style}">{encoded}</span>""";
    }
}