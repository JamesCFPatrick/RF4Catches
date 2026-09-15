using OpenCvSharp;
using Size = System.Drawing.Size;

namespace RF4Catches.Services;

public sealed class ScreenCaptureService(IWebHostEnvironment environment)
{
    private readonly string _captureDirectory = Path.Combine(environment.ContentRootPath, "data", "captures");
    private readonly string _catchImageDirectory = Path.Combine(environment.ContentRootPath, "data", "catch-images");

    public Task<string> CaptureVirtualScreenAsync(
        CancellationToken cancellationToken = default,
        bool persist = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = persist ? _captureDirectory : Path.GetTempPath();
        Directory.CreateDirectory(directory);
        var primaryScreen = Screen.PrimaryScreen
            ?? throw new InvalidOperationException("Windows could not identify a primary display.");
        var bounds = primaryScreen.Bounds;
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);

        var prefix = persist ? "catch-" : "rf4catches-scan-";
        var path = Path.Combine(directory, $"{prefix}{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.png");
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return Task.FromResult(path);
    }

    public Task<string> PersistCaptureAsync(string temporaryPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(temporaryPath))
            throw new FileNotFoundException("The temporary capture no longer exists.", temporaryPath);

        Directory.CreateDirectory(_captureDirectory);
        var persistedPath = Path.Combine(_captureDirectory, $"catch-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.png");
        File.Copy(temporaryPath, persistedPath);
        File.Delete(temporaryPath);
        return Task.FromResult(persistedPath);
    }

    public Task<string> PersistCatchImageAsync(
        string capturePath,
        OcrRegion region,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(capturePath))
            throw new FileNotFoundException("The capture no longer exists.", capturePath);

        using var source = new Bitmap(capturePath);
        var crop = Rectangle.FromLTRB(
            (int)Math.Floor(source.Width * region.X),
            (int)Math.Floor(source.Height * region.Y),
            (int)Math.Ceiling(source.Width * (region.X + region.Width)),
            (int)Math.Ceiling(source.Height * (region.Y + region.Height)));
        crop.Intersect(new Rectangle(0, 0, source.Width, source.Height));
        if (crop.Width < 1 || crop.Height < 1)
            throw new InvalidOperationException("The fish image region is empty.");

        const int maximumDimension = 320;
        var scale = Math.Min(1d, Math.Min((double)maximumDimension / crop.Width, (double)maximumDimension / crop.Height));
        var size = new Size(
            Math.Max(1, (int)Math.Round(crop.Width * scale)),
            Math.Max(1, (int)Math.Round(crop.Height * scale)));
        using var resized = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, new Rectangle(System.Drawing.Point.Empty, size), crop, GraphicsUnit.Pixel);
        }

        Directory.CreateDirectory(_catchImageDirectory);
        var fileName = $"fish-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.jpg";
        var path = Path.Combine(_catchImageDirectory, fileName);
        var codec = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
            .Single(item => item.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
        using var parameters = new System.Drawing.Imaging.EncoderParameters(1);
        parameters.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 72L);
        resized.Save(path, codec, parameters);
        return Task.FromResult(fileName);
    }
    
    public OcrRegion? FindTextRow(string capturePath, OcrRegion searchRegion)
{
    if (!File.Exists(capturePath)) return null;

    using var source = Cv2.ImRead(capturePath, ImreadModes.Grayscale);
    if (source.Empty()) return null;

    var bandX = Math.Max(0, (int)Math.Floor(source.Width * searchRegion.X));
    var bandY = Math.Max(0, (int)Math.Floor(source.Height * searchRegion.Y));
    var bandW = Math.Min(source.Width - bandX, (int)Math.Ceiling(source.Width * searchRegion.Width));
    var bandH = Math.Min(source.Height - bandY, (int)Math.Ceiling(source.Height * searchRegion.Height));
    if (bandW < 8 || bandH < 8) return null;

    using var crop = new Mat(source, new Rect(bandX, bandY, bandW, bandH));

    // Keep only the bright text; everything else goes black.
    using var mask = new Mat();
    Cv2.Threshold(crop, mask, 180, 255, ThresholdTypes.Binary);

    // Merge glyphs into a single blob per word.
    using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(25, 3));
    using var closed = new Mat();
    Cv2.MorphologyEx(mask, closed, MorphTypes.Close, kernel);

    Cv2.FindContours(closed, out var contours, out _,
        RetrievalModes.External, ContourApproximationModes.ApproxSimple);

    Rect? best = null;
    foreach (var contour in contours)
    {
        var r = Cv2.BoundingRect(contour);
        if (r.Height < bandH * 0.25) continue;
        if (r.Height > bandH * 0.95) continue;
        if (r.Width < bandW * 0.20) continue;
        if (r.Width > bandW * 0.95) continue;
        var aspect = (double)r.Width / r.Height;
        if (aspect < 1.8 || aspect > 6.0) continue;   // NEW
        if (best is null || r.Width * r.Height > best.Value.Width * best.Value.Height)
            best = r;
    }

    if (best is not { } box) return null;

    // Small padding so Tesseract has breathing room.
    // Blob gives us a center point; expand to a minimum size so the whole
// "number + unit" pair is contained even if morphological closing only
// merged the digits.
    var centerX = box.X + box.Width / 2;
    var centerY = box.Y + box.Height / 2;

    var minW = (int)(bandW * 0.90);   // was 0.70
    var minH = (int)(bandH * 0.80);   // was 0.60

    var finalW = Math.Max(box.Width + 12, minW);
    var finalH = Math.Max(box.Height + 8, minH);
    finalW = Math.Min(bandW, finalW);
    finalH = Math.Min(bandH, finalH);

    var finalX = Math.Clamp(centerX - finalW / 2, 0, bandW - finalW);
    var finalY = Math.Clamp(centerY - finalH / 2, 0, bandH - finalH);

    return new OcrRegion(
        (bandX + finalX) / (double)source.Width,
        (bandY + finalY) / (double)source.Height,
        finalW / (double)source.Width,
        finalH / (double)source.Height);
}

    /// <summary>
/// Returns every rarity tag whose colour is present in the region, in
/// stable display order: Valuable, Trophy, Rare Trophy, Rare.
/// Empty when nothing clears the threshold.
/// </summary>
public IReadOnlyList<string> DetectTagRarities(string capturePath, OcrRegion region)
{
    if (!File.Exists(capturePath))
        throw new FileNotFoundException("The capture no longer exists.", capturePath);

    using var source = new Bitmap(capturePath);
    var crop = Rectangle.FromLTRB(
        (int)Math.Floor(source.Width * region.X),
        (int)Math.Floor(source.Height * region.Y),
        (int)Math.Ceiling(source.Width * (region.X + region.Width)),
        (int)Math.Ceiling(source.Height * (region.Y + region.Height)));
    crop.Intersect(new Rectangle(0, 0, source.Width, source.Height));
    if (crop.Width < 1 || crop.Height < 1)
        return Array.Empty<string>();

    var greenPixels = 0;
    var bluePixels = 0;
    var purplePixels = 0;
    var yellowPixels = 0;
    var sampledPixels = 0;

    for (var y = crop.Top; y < crop.Bottom; y += 2)
    {
        for (var x = crop.Left; x < crop.Right; x += 2)
        {
            var pixel = source.GetPixel(x, y);
            var max = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) / 255d;
            var min = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B)) / 255d;
            var saturation = max == 0 ? 0 : (max - min) / max;
            var hue = GetHue(pixel.R / 255d, pixel.G / 255d, pixel.B / 255d, max, min);
            if (max >= 0.35)
            {
                var isVivid = saturation >= 0.35;
                var isPastel = saturation >= 0.22;
                if (hue is >= 40 and < 55 && isVivid) yellowPixels++;
                else if (hue is >= 55 and <= 155 && isVivid) greenPixels++;
                else if (hue is >= 190 and < 245 && isVivid) bluePixels++;
                else if (hue is >= 245 and <= 330 && isPastel) purplePixels++;
            }
            sampledPixels++;
        }
    }

    var hits = new List<string>(4);
    if (MeetsThreshold(greenPixels, sampledPixels)) hits.Add("Valuable");
    if (MeetsThreshold(yellowPixels, sampledPixels)) hits.Add("Trophy");
    if (MeetsThreshold(bluePixels, sampledPixels)) hits.Add("Rare Trophy");
    if (MeetsThreshold(purplePixels, sampledPixels)) hits.Add("Rare");
    return hits;

    static bool MeetsThreshold(int pixels, int sampled) =>
        pixels >= 100 && pixels / (double)sampled >= 0.02;
}
    
    // FIND ICON - finds template icons
    public (double X, double Y)? FindIcon(string capturePath, OcrRegion searchRegion, string templatePath, double threshold = 0.75)
    {
        if (!File.Exists(capturePath) || !File.Exists(templatePath)) return null;

        using var source = Cv2.ImRead(capturePath, ImreadModes.Grayscale);
        using var template = Cv2.ImRead(templatePath, ImreadModes.Grayscale);
        if (source.Empty() || template.Empty()) return null;

        var bandX = Math.Max(0, (int)(source.Width * searchRegion.X));
        var bandY = Math.Max(0, (int)(source.Height * searchRegion.Y));
        var bandW = Math.Min(source.Width - bandX, (int)(source.Width * searchRegion.Width));
        var bandH = Math.Min(source.Height - bandY, (int)(source.Height * searchRegion.Height));
        if (bandW < template.Width || bandH < template.Height) return null;

        using var band = new Mat(source, new Rect(bandX, bandY, bandW, bandH));

        using var result = new Mat();
        Cv2.MatchTemplate(band, template, result, TemplateMatchModes.CCoeffNormed);

        Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);
        if (maxVal < threshold) return null;

        // Return the *center* of the icon in image-relative coordinates
        var centerX = bandX + maxLoc.X + template.Width / 2.0;
        var centerY = bandY + maxLoc.Y + template.Height / 2.0;
        return (centerX / source.Width, centerY / source.Height);
    }
    
    private static double GetHue(double red, double green, double blue, double max, double min)
    {
        if (max == min) return 0;
        var delta = max - min;
        var hue = max == red
            ? 60 * ((green - blue) / delta % 6)
            : max == green
                ? 60 * ((blue - red) / delta + 2)
                : 60 * ((red - green) / delta + 4);
        return hue < 0 ? hue + 360 : hue;
    }
    
    /// <summary>
    /// Best-effort delete of a temporary capture. Never throws;
    /// the retention service will pick up leftovers if this fails.
    /// </summary>
    public void TryDeleteCapture(string capturePath)
    {
        if (string.IsNullOrWhiteSpace(capturePath)) return;
        try
        {
            if (File.Exists(capturePath)) File.Delete(capturePath);
        }
        catch (IOException) { /* locked or in use — retention will clean later */ }
        catch (UnauthorizedAccessException) { /* same */ }
    }
    
    
    public void DeleteCapture(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    public int DeletePersistedCaptures(IReadOnlySet<string>? preservedPaths = null)
    {
        if (!Directory.Exists(_captureDirectory)) return 0;

        var deletedCount = 0;
        foreach (var path in Directory.EnumerateFiles(_captureDirectory, "catch-*.png"))
        {
            if (preservedPaths?.Contains(path) == true) continue;
            File.Delete(path);
            deletedCount++;
        }

        return deletedCount;
    }
}
