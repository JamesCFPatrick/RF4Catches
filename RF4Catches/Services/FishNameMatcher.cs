namespace RF4Catches.Services;

/// <summary>
/// Resolves OCR species text to a canonical fish name.
/// Add species and OCR variants to <see cref="FishCatalog"/> instead of
/// adding parser-specific corrections.
/// </summary>
public sealed class FishNameMatcher
{
    private static readonly FishEntry[] FishCatalog =
    [
        new("Albino Catfish", ["albino catfish"]),
        new("Arctic Char", ["arctic char", "artic char"]),
        new("Arctic Grayling", ["arctic grayling", "artic grayling"]),
        new("Arctic Omul", ["arctic omul", "artic omul"]),
        new("Asp", ["asp"]),
        new("Butterfly Peacock bass", ["butterfly peacock bass"]),
        new("Hybrid Striped Bass Whiterock Bass", ["hybrid striped bass whiterock bass", "hybrid striped bass"]),
        new("Largemouth bass", ["largemouth bass"]),
        new("Smallmouth bass", ["smallmouth bass"]),
        new("White crappie", ["white crappie"]),
        new("Asian Smelt", ["asian smelt"]),
        new("Atlantic Salmon", ["atlantic salmon"]),
        new("Amur Catfish", ["amur catfish"]),
        new("Bastard Sturgeon", ["bastard sturgeon"]),
        new("Baikal Omul", ["baikal omul"]),
        new("Beloribitsa Whitefish", ["beloribitsa whitefish"]),
        new("Beluga", ["beluga"]),
        new("Bighead Carp", ["bighead carp"]),
        new("Black Carp", ["black carp"]),
        new("Black Sea Beluga", ["black sea beluga"]),
        new("Black Sea Kutum", ["black sea kutum"]),
        new("Black Sea Shemaya", ["black sea shemaya"]),
        new("Black-spined Herring", ["black-spined herring"]),
        new("Black Buffalo", ["black buffalo"]),
        new("Black Whitefish", ["black whitefish"]),
        new("Bleak", ["bleak"]),
        new("Bluegill", ["bluegill"]),
        new("Blue Bream", ["blue bream"]),
        new("Bream", ["bream"]),
        new("Broad Whitefish", ["broad whitefish"]),
        new("Brown Trout", ["brown trout"]),
        new("Buffalo", ["buffalo"]),
        new("Burbot", ["burbot"]),
        new("Caspian Brown Trout", ["caspian brown trout"]),
        new("Caspian Kutum", ["caspian kutum"]),
        new("Caspian Lamprey", ["caspian lamprey"]),
        new("Caspian Roach", ["caspian roach"]),
        new("Catfish", ["catfish"]),
        new("Char", ["char"]),
        new("Channel catfish", ["channel catfish"]),
        new("Chinook Salmon", ["chinook salmon"]),
        new("Chinese Sleeper", ["chinese sleeper"]),
        new("Chub", ["chub"]),
        new("Chum Salmon", ["chum salmon"]),
        new("Clupeonella", ["clupeonella"]),
        new("Coho Salmon", ["coho salmon"]),
        new("Common Barbel", ["common barbel"]),
        new("Common Carp", ["common carp"]),
        new("Common Carp Ghost", ["common carp ghost"]),
        new("Common Minnow", ["common minnow"]),
        new("Common Roach", ["common roach"]),
        new("Common Scaly Albino Carp", ["common scaly albino carp"]),
        new("Crucian Carp", ["crucian carp"]),
        new("Dace", ["dace"]),
        new("Donets Ruffe", ["donets ruffe"]),
        new("Dolly Varden Trout", ["dolly varden trout"]),
        new("Dryagin Char", ["dryagin char"]),
        new("Eastern Bream", ["eastern bream"]),
        new("East Siberian Grayling", ["east siberian grayling"]),
        new("East Siberian Sturgeon", ["east siberian sturgeon"]),
        new("Eel", ["eel"]),
        new("Far Eastern Brook Lamprey", ["far eastern brook lamprey"]),
        new ("Flathead Catfish", ["flathead catfish"]),
        new("Frame-sided Albino Carp", ["frame-sided albino carp"]),
        new("Frame-sided Carp", ["frame-sided carp"]),
        new("Frame-sided Ghost Carp", ["frame-sided ghost carp"]),
        new("Gibel Carp", ["gibel carp"]),
        new("Golden Tench", ["golden tench"]),
        new("Grass Carp", ["grass carp"]),
        new("Gray Char", ["gray char"]),
        new("Grayling", ["grayling"]),
        new("Gudgeon", ["gudgeon"]),
        new("Humpback Whitefish", ["humpback whitefish"]),
        new("Ide", ["ide"]),
        new("Kamchatkan Rainbow Trout", ["kamchatkan rainbow trout"]),
        new("Kaluga", ["kaluga"]),
        new("Kessler's Herring", ["kessler's herring", "kesslers herring"]),
        new("Kuori Char", ["kuori char"]),
        new("Kuori Whitefish", ["kuori whitefish"]),
        new("Lake Minnow", ["lake minnow"]),
        new("Lake Trout", ["lake trout"]),
        new("Ladoga Lake Whitefish", ["ladoga lake whitefish"]),
        new("Ladoga Salmon", ["ladoga salmon"]),
        new("Ladoga Sturgeon", ["ladoga sturgeon"]),
        new("Leather Carp", ["leather carp"]),
        new("Levanidov's Char", ["levanidov's char", "levanidovs char"]),
        new("Linear Albino Carp", ["linear albino carp"]),
        new("Linear Carp", ["linear carp"]),
        new("Loach", ["loach"]),
        new("Ludoga Whitefish", ["ludoga whitefish"]),
        new("Mirror Albino Carp", ["mirror albino carp"]),
        new("Mirror Carp", ["mirror carp"]),
        new("Mirror Ghost Carp", ["mirror ghost carp"]),
        new("Muksun", ["muksun"]),
        new("Nase", ["nase"]),
        new("Nelma", ["nelma"]),
        new("Nine-Spined Stickleback", ["nine-spined stickleback"]),
        new("Peled", ["peled"]),
        new("Perch", ["perch"]),
        new("Persian Sturgeon", ["persian sturgeon"]),
        new("Pike", ["pike"]),
        new("Pink Salmon", ["pink salmon"]),
        new("Pontic Shad", ["pontic shad"]),
        new("Pumpkinseed Sunfish", ["pumpkinseed sunfish"]),
        new("Rainbow Trout", ["rainbow trout"]),
        new("Red Char", ["red char"]),
        new("Red Starvas Carp - Scaly", ["red starvas carp - scaly", "red starvas carp scaly"]),
        new("Ripus", ["ripus"]),
        new("Round Whitefish", ["round whitefish"]),
        new("Rudd", ["rudd"]),
        new("Ruffe", ["ruffe"]),
        new("Russian Sturgeon", ["russian sturgeon"]),
        new("Scaleless Albino Carp", ["scaleless albino carp"]),
        new("Scaleless Ghost Carp", ["scaleless ghost carp"]),
        new("Sevan Trout", ["sevan trout"]),
        new("Siberian Char Loach", ["siberian char loach"]),
        new("Siberian Dace", ["siberian dace"]),
        new("Siberian Gudgeon", ["siberian gudgeon"]),
        new("Siberian Lamprey", ["siberian lamprey"]),
        new("Siberian Roach", ["siberian roach"]),
        new("Siberian Sardine Cisco", ["siberian sardine cisco", "syberian sardine cisco"]),
        new("Siberian Sculpin", ["siberian sculpin"]),
        new("Siberian Sterlet", ["siberian sterlet", "syberian sterlet"]),
        new("Sharp-snouted Lenok", ["sharp-snouted lenok"]),
        new("Shemaya", ["shemaya"]),
        new("Short-headed Barbel", ["short-headed barbel"]),
        new("Sichel", ["sichel"]),
        new("Silver Bream", ["silver bream"]),
        new("Silver Carp", ["silver carp"]),
        new("Small Southern Stickleback", ["small southern stickleback"]),
        new("Smelt", ["smelt"]),
        new("Sockeye Salmon", ["sockeye salmon"]),
        new("Svir Whitefish", ["svir whitefish"]),
        new("Stellate Sturgeon", ["stellate sturgeon"]),
        new("Sterlet", ["sterlet"]),
        new("Starvas Red Carp - Mirror", ["starvas red carp - mirror", "starvas red carp mirror"]),
        new("Taimen", ["taimen"]),
        new("Taran", ["taran"]),
        new("Tench", ["tench"]),
        new("Three-Spined Stickleback", ["three-spined stickleback"]),
        new("Three-Toothed Lamprey", ["three-toothed lamprey"]),
        new("Tugun", ["tugun"]),
        new("Ukrainian Lamprey", ["ukrainian lamprey"]),
        new("Valaam Whitefish", ["valaam whitefish"]),
        new("Vendace", ["vendace"]),
        new("Volga Zander", ["volga zander"]),
        new("Volkhov Whitefish", ["volkhov whitefish"]),
        new("Vuoksa Whitefish", ["vuoksa whitefish"]),
        new("Vimba", ["vimba"]),
        new("White Bream", ["white bream"]),
        new("White-eye Bream", ["white-eye bream", "white eye bream"]),
        new("Whitespotted Char", ["whitespotted char"]),
        new("Wild Carp", ["wild carp"]),
        new("Zander", ["zander"]),
        new("Warmouth", ["warmouth", "wet telli"])
    ];

    /// <summary>
    /// Returns the closest catalog name when the edit distance is conservative
    /// enough; otherwise returns the cleaned OCR text unchanged.
    /// </summary>
    public string Normalize(string value)
    {
        var cleaned = TextCleanup.CleanFishName(value);
        if (cleaned.Length < 3) return cleaned;

        var normalized = NormalizeForComparison(cleaned);
        var bestMatch = FishCatalog
            .SelectMany(fish => fish.Variants.Select(variant => new
            {
                fish.CanonicalName,
                Distance = LevenshteinDistance(normalized, NormalizeForComparison(variant))
            }))
            .OrderBy(match => match.Distance)
            .FirstOrDefault();

        if (bestMatch is null) return cleaned;
        var maximumDistance = bestMatch.CanonicalName.Length <= 6 ? 1 : 2;
        return bestMatch.Distance <= maximumDistance ? bestMatch.CanonicalName : cleaned;
    }

    private static string NormalizeForComparison(string value) =>
        string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();

    private static int LevenshteinDistance(string left, string right)
    {
        var distances = new int[left.Length + 1, right.Length + 1];
        for (var i = 0; i <= left.Length; i++) distances[i, 0] = i;
        for (var j = 0; j <= right.Length; j++) distances[0, j] = j;

        for (var i = 1; i <= left.Length; i++)
        {
            for (var j = 1; j <= right.Length; j++)
            {
                var substitutionCost = left[i - 1] == right[j - 1] ? 0 : 1;
                distances[i, j] = Math.Min(
                    Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                    distances[i - 1, j - 1] + substitutionCost);
            }
        }

        return distances[left.Length, right.Length];
    }

    private sealed record FishEntry(string CanonicalName, IReadOnlyList<string> Variants);
}
