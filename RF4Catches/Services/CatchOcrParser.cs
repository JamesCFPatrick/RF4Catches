using System.Globalization;
using System.Text.RegularExpressions;

namespace RF4Catches.Services;

public sealed record ParsedCatch(string? Species, decimal? WeightKg, decimal? LengthCm, string? Rarity)
{
    public bool HasCatchDetails => Species is { Length: >= 3 } && WeightKg is not null;
}

public sealed partial class CatchOcrParser(FishNameMatcher fishNameMatcher, ILogger<CatchOcrParser> logger)
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
        const decimal maxReasonableKg = 80m;   // Adjust later if needed

        var match = WeightPattern().Match(detailsText);
        if (match.Success)
        {
            var value = ParseDecimal(match);
            if (value is null) return null;

            var unit = match.Groups["unit"].Value;
            decimal weightKg;

            if (string.Equals(unit, "g", StringComparison.OrdinalIgnoreCase))
            {
                weightKg = value.Value / 1000m;
            }
            else
            {
                // Declared as kg
                weightKg = CatchWeight.NormalizeKg(value.Value);
            }

            // Sanity check
            if (weightKg > maxReasonableKg)
            {
                // Almost certainly the unit was missed and this is grams
                logger.LogWarning(
                    "Suspicious weight {WeightKg:F3} kg detected (>{Max} kg). Treating as grams. Raw OCR: {Text}",
                    weightKg, maxReasonableKg, detailsText);

                return weightKg / 1000m;
            }

            return weightKg;
        }

        // Fallback pattern (e.g. "4319" misread)
        var fallback = WeightMisreadPattern().Match(detailsText);
        if (fallback.Success && ParseDecimal(fallback) is { } grams)
        {
            if (grams > 80_000) // > 80 kg in grams
            {
                logger.LogWarning("Extremely large fallback weight ignored: {Grams} g. Text: {Text}", grams, detailsText);
                return null;
            }
            return grams / 1000m;
        }

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
