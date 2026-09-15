using System.Globalization;
using System.Text.RegularExpressions;

namespace RF4Catches.Services;

public sealed record ParsedCatch(string? Species, decimal? WeightKg, decimal? LengthCm, string? Rarity)
{
    public bool HasCatchDetails => Species is { Length: >= 3 } && WeightKg is not null;
}

public sealed partial class CatchOcrParser(FishNameMatcher fishNameMatcher)
{
    [GeneratedRegex(@"(?<value>\d+(?:[.,]\d+)?)\s*(?<unit>kg|g)\b", RegexOptions.IgnoreCase)]
    private static partial Regex WeightPattern();

    [GeneratedRegex(@"(?<value>\d+(?:[.,]\d+)?)\s*cm\b", RegexOptions.IgnoreCase)]
    private static partial Regex LengthPattern();

    [GeneratedRegex(@"\b(trophy|valuable|erectile|common|uncommon|rare)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RarityPattern();
    
    [GeneratedRegex(@"(?<value>\d+(?:[.,]\d+)?)9\b")]
    private static partial Regex WeightMisreadPattern();

    public ParsedCatch Parse(string rawText, string? detectedRarity = null)
    {
        var weightMatch = WeightPattern().Match(rawText);
        var rarityMatch = RarityPattern().Match(rawText);

        var speciesText = weightMatch.Success ? rawText[..weightMatch.Index] : "";
        if (rarityMatch.Success && rarityMatch.Index < speciesText.Length)
            speciesText = speciesText.Remove(rarityMatch.Index, rarityMatch.Length);

        return ParseFields(speciesText, rawText, detectedRarity);
    }

    public ParsedCatch ParseFields(string speciesText, string detailsText, string? detectedRarity = null)
    {
        var weightMatch = WeightPattern().Match(detailsText);
        var lengthMatch = LengthPattern().Match(detailsText);
        var rarityMatch = RarityPattern().Match(detailsText);
        var species = fishNameMatcher.Normalize(speciesText);

        return new ParsedCatch(
            string.IsNullOrWhiteSpace(species) ? null : species,
            ParseWeightKg(detailsText),
            ParseDecimal(lengthMatch),
            detectedRarity ?? NormalizeRarity(rarityMatch));
    }

    private decimal? ParseWeightKg(string detailsText)
    {
        var match = WeightPattern().Match(detailsText);
        if (match.Success)
        {
            var value = ParseDecimal(match);
            if (value is null) return null;
            if (string.Equals(match.Groups["unit"].Value, "g", StringComparison.OrdinalIgnoreCase))
                return value / 1000m;
            return value is { } weight ? CatchWeight.NormalizeKg(weight) : null;
        }

        var fallback = WeightMisreadPattern().Match(detailsText);
        if (fallback.Success && ParseDecimal(fallback) is { } grams)
            return grams / 1000m;

        return null;
    }

    private static decimal? ParseDecimal(Match match) =>
        match.Success && decimal.TryParse(match.Groups["value"].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static string? NormalizeRarity(Match match)
    {
        if (!match.Success) return null;
        return match.Value.Equals("erectile", StringComparison.OrdinalIgnoreCase)
            ? "Valuable"
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(match.Value.ToLowerInvariant());
    }
}
