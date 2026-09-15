using System.Text;
using System.Text.RegularExpressions;

namespace RF4Catches.Services;

public static partial class TextCleanup
{
    /// <summary>
    /// Removes OCR punctuation and collapses whitespace without applying
    /// species-specific rules.
    /// </summary>
    public static string CleanFishName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize())
        {
            if (char.IsLetterOrDigit(character) || char.IsWhiteSpace(character) ||
                character is '\'' or '-' or '.')
                builder.Append(character);
        }

        var cleaned = WhitespacePattern().Replace(builder.ToString(), " ").Trim(' ', '.', '-', '\'');
        return cleaned;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
