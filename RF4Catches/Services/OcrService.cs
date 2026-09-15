using OpenCvSharp;

namespace RF4Catches.Services;

public sealed class OcrOptions
{
    public string DataPath { get; init; } = "tessdata";
    public string Language { get; init; } = "rus+eng";
}

public sealed record OcrResult(string Text, float Confidence);
public sealed record OcrRegion(double X, double Y, double Width, double Height);

public sealed record CatchCardOcr(
    OcrResult Result,
    string? SpeciesText,
    string? WeightText,
    string? LengthText)
{
    public string? DetailsText => WeightText is null || LengthText is null
        ? null
        : $"{WeightText} {LengthText}".Trim();
    public bool IsSplit => SpeciesText is not null && WeightText is not null && LengthText is not null;
}

public sealed class OcrService(Microsoft.Extensions.Options.IOptions<OcrOptions> options, IWebHostEnvironment environment) : IDisposable
{
    private readonly OcrOptions _options = options.Value;
    private readonly string _dataPath = Path.GetFullPath(options.Value.DataPath, environment.ContentRootPath);
    private readonly object _engineLock = new();
    private Tesseract.TesseractEngine? _engine;

    public OcrResult Read(string imagePath, string? whitelist = null, Tesseract.PageSegMode pageSegMode = Tesseract.PageSegMode.Auto)
    {
        var missing = _options.Language.Split('+')
            .Where(language => !File.Exists(Path.Combine(_dataPath, $"{language}.traineddata"))).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Tesseract language data is missing: {string.Join(", ", missing)}. Place it in '{_dataPath}'.");

        lock (_engineLock)
        {
            var engine = GetEngine();
            engine.SetVariable("tessedit_char_whitelist", whitelist ?? "");
            using var image = Tesseract.Pix.LoadFromFile(imagePath);
            using var page = engine.Process(image, pageSegMode);
            var text = page.GetText().Trim();
            return new OcrResult(text, string.IsNullOrWhiteSpace(text) ? 0f : page.GetMeanConfidence());
        }
    }

    public CatchCardOcr ReadCatchCard(
        string imagePath,
        OcrRegion speciesRegion,
        OcrRegion detailsRegion)
    {
        var species = ReadRegion(
            imagePath,
            speciesRegion,
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz '-.",
            Tesseract.PageSegMode.SingleLine);
        var details = ReadRegion(
            imagePath,
            detailsRegion,
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,[]'-",
            Tesseract.PageSegMode.SingleLine);
        return new CatchCardOcr(
            new OcrResult($"{species.Text}\n{details.Text}".Trim(), Math.Min(species.Confidence, details.Confidence)),
            species.Text,
            details.Text,
            string.Empty);
    }

    public CatchCardOcr ReadRefinedCatch(
        string imagePath,
        OcrRegion speciesRegion,
        OcrRegion weightRegion,
        OcrRegion lengthRegion)
    {
        var speciesOcr = ReadRegion(imagePath, speciesRegion,
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz '-.",
            Tesseract.PageSegMode.SingleLine);
        var weightOcr = ReadRegion(imagePath, weightRegion,
            "0123456789.,kg ",
            Tesseract.PageSegMode.SingleLine);
        var lengthOcr = ReadRegion(imagePath, lengthRegion,
            "0123456789.,cm ",
            Tesseract.PageSegMode.SingleLine);

        var detailsText = $"{weightOcr.Text} {lengthOcr.Text}".Trim();
        return new CatchCardOcr(
            new OcrResult($"{speciesOcr.Text}\n{detailsText}".Trim(),
                Math.Min(speciesOcr.Confidence, Math.Min(weightOcr.Confidence, lengthOcr.Confidence))),
            speciesOcr.Text,
            weightOcr.Text,
            lengthOcr.Text);
    }

    private static RoiProfile? FindProfile(IReadOnlyList<RoiProfile> profiles, string name) =>
        profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private Tesseract.TesseractEngine GetEngine()
    {
        if (_engine is not null) return _engine;

        var missing = _options.Language.Split('+')
            .Where(language => !File.Exists(Path.Combine(_dataPath, $"{language}.traineddata"))).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Tesseract language data is missing: {string.Join(", ", missing)}. Place it in '{_dataPath}'.");

        _engine = new Tesseract.TesseractEngine(_dataPath, _options.Language, Tesseract.EngineMode.LstmOnly);
        _engine.SetVariable(
            "tessedit_char_whitelist",
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,;:'\\-[]()+_");
        return _engine;
    }

    public sealed record OcrCandidate(string Text, float Confidence, bool IsOriginal);

public IReadOnlyList<OcrCandidate> ReadRegionCandidates(
    string imagePath,
    OcrRegion region,
    string? whitelist = null,
    Tesseract.PageSegMode pageSegMode = Tesseract.PageSegMode.Auto)
{
    if (region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0 ||
        region.X + region.Width > 1 || region.Y + region.Height > 1)
        throw new ArgumentOutOfRangeException(nameof(region), "The crop must stay inside the image.");

    using var source = new Bitmap(imagePath);
    var crop = Rectangle.FromLTRB(
        (int)Math.Floor(source.Width * region.X),
        (int)Math.Floor(source.Height * region.Y),
        (int)Math.Ceiling(source.Width * (region.X + region.Width)),
        (int)Math.Ceiling(source.Height * (region.Y + region.Height)));

    crop.Intersect(new Rectangle(0, 0, source.Width, source.Height));
    if (crop.Width < 1 || crop.Height < 1)
        throw new InvalidOperationException("The selected crop is empty.");

    var temporaryPath = Path.Combine(Path.GetTempPath(), $"rf4catches-{Guid.NewGuid():N}.png");
    var processedPaths = new List<string>();
    try
    {
        using var cropped = source.Clone(crop, source.PixelFormat);
        cropped.Save(temporaryPath, System.Drawing.Imaging.ImageFormat.Png);

        var original = Read(temporaryPath, whitelist, pageSegMode);
        var candidates = new List<OcrCandidate>
        {
            new(original.Text, original.Confidence, IsOriginal: true)
        };

        if (original.Confidence >= 0.85f)
            return candidates;

        processedPaths.AddRange(CreatePreprocessedVariants(temporaryPath));
        foreach (var processedPath in processedPaths)
        {
            var result = Read(processedPath, whitelist, pageSegMode);
            if (string.IsNullOrWhiteSpace(result.Text)) continue;
            candidates.Add(new OcrCandidate(result.Text, result.Confidence, IsOriginal: false));
        }

        return candidates
            .OrderByDescending(c => c.Confidence)
            .ToList();
    }
    finally
    {
        if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        foreach (var processedPath in processedPaths)
            if (File.Exists(processedPath)) File.Delete(processedPath);
    }
}

// Keep the old API for existing callers that don't care about validation
public OcrResult ReadRegion(
    string imagePath,
    OcrRegion region,
    string? whitelist = null,
    Tesseract.PageSegMode pageSegMode = Tesseract.PageSegMode.Auto)
{
    var candidates = ReadRegionCandidates(imagePath, region, whitelist, pageSegMode);
    var best = candidates.Count == 0 ? new OcrCandidate("", 0f, IsOriginal: true) : candidates[0];
    return new OcrResult(best.Text, best.Confidence);
}

    private static IReadOnlyList<string> CreatePreprocessedVariants(string sourcePath)
    {
        using var source = Cv2.ImRead(sourcePath, ImreadModes.Grayscale);
        if (source.Empty())
            throw new InvalidOperationException("OpenCV could not read the OCR crop.");

        using var enlarged = new Mat();
        Cv2.Resize(source, enlarged, new OpenCvSharp.Size(), 2, 2, InterpolationFlags.Lanczos4);

        using var contrast = new Mat();
        using (var clahe = Cv2.CreateCLAHE(2.0, new OpenCvSharp.Size(8, 8)))
            clahe.Apply(enlarged, contrast);

        using var otsu = new Mat();
        Cv2.Threshold(contrast, otsu, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        using var adaptive = new Mat();
        Cv2.AdaptiveThreshold(
            contrast,
            adaptive,
            255,
            AdaptiveThresholdTypes.GaussianC,
            ThresholdTypes.Binary,
            31,
            7);

        using var inverted = new Mat();
        Cv2.BitwiseNot(otsu, inverted);

        var paths = new List<string>();
        SaveVariant(enlarged, paths);
        SaveVariant(contrast, paths);
        SaveVariant(otsu, paths);
        SaveVariant(adaptive, paths);
        SaveVariant(inverted, paths);
        return paths;
    }

    private static void SaveVariant(Mat image, ICollection<string> paths)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rf4catches-opencv-{Guid.NewGuid():N}.png");
        Cv2.ImWrite(path, image);
        paths.Add(path);
    }

    public void Dispose()
    {
        lock (_engineLock)
        {
            _engine?.Dispose();
            _engine = null;
        }
    }
}
