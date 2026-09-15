using RF4Catches.Services;

namespace RF4Catches.Endpoints;

public static class RoiEndpoints
{
    public static void MapRoiEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/roi", () => Results.Redirect("/roi.html"));

        app.MapPost("/roi/upload", async Task<IResult> (IFormFile image, IWebHostEnvironment environment, CancellationToken ct) =>
        {
            var extension = Path.GetExtension(image.FileName);
            var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" };

            if (image.Length == 0 || image.Length > 20 * 1024 * 1024 || !allowedExtensions.Contains(extension))
                return Results.BadRequest("Upload a PNG, JPEG, BMP, or TIFF image smaller than 20 MB.");

            var directory = Path.Combine(environment.ContentRootPath, "data", "ocr-samples");
            Directory.CreateDirectory(directory);
            var fileName = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}{extension}";
            await using var stream = File.Create(Path.Combine(directory, fileName));
            await image.CopyToAsync(stream, ct);
            return Results.Content(RoiEditorMarkup.Editor(fileName), "text/html");
        }).DisableAntiforgery();

        app.MapGet("/roi/image/{fileName}", (string fileName, IWebHostEnvironment environment) =>
        {
            if (!string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
                return Results.BadRequest();
            var path = Path.Combine(environment.ContentRootPath, "data", "ocr-samples", fileName);
            return File.Exists(path) ? Results.File(path) : Results.NotFound();
        });

        app.MapPost("/roi/test", ([Microsoft.AspNetCore.Mvc.FromForm] RoiForm form, OcrService ocr, CatchOcrParser catchOcrParser, ScreenCaptureService screenCapture, IWebHostEnvironment environment) =>
        {
            var path = Path.Combine(environment.ContentRootPath, "data", "ocr-samples", Path.GetFileName(form.Image));
            if (!File.Exists(path))
                return Results.Content(RoiEditorMarkup.Error("The uploaded image no longer exists."), "text/html");

            try
            {
                var result = ocr.ReadRegion(path, form.ToRegion());
                var detectedRarities = screenCapture.DetectTagRarities(path, form.ToRegion()).ToDisplayString();
                return Results.Content(RoiEditorMarkup.Result(result, catchOcrParser.Parse(result.Text, detectedRarities)), "text/html");
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.Content(RoiEditorMarkup.Error(ex.Message), "text/html");
            }
        }).DisableAntiforgery();

        app.MapPost("/roi/save", async Task<IResult> ([Microsoft.AspNetCore.Mvc.FromForm] RoiForm form, RoiProfileStore profiles, CancellationToken ct) =>
        {
            try
            {
                await profiles.SaveAsync(form.Name, form.ToRegion(), ct);
                return Results.Content(RoiEditorMarkup.Profiles(await profiles.GetAllAsync(ct)), "text/html");
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
            {
                return Results.Content(RoiEditorMarkup.Error(ex.Message), "text/html");
            }
        }).DisableAntiforgery();

        app.MapPost("/roi/delete", async Task<IResult> (
            [Microsoft.AspNetCore.Mvc.FromForm] RoiDeleteForm form,
            RoiProfileStore profiles,
            CancellationToken ct) =>
        {
            await profiles.DeleteAsync(form.Name, ct);
            return Results.Content(RoiEditorMarkup.Profiles(await profiles.GetAllAsync(ct)), "text/html");
        }).DisableAntiforgery();

        app.MapGet("/roi/profiles", async (RoiProfileStore profiles, CancellationToken ct) =>
            Results.Content(RoiEditorMarkup.Profiles(await profiles.GetAllAsync(ct)), "text/html"));
    }
}

public sealed class RoiForm
{
    public string Name { get; init; } = "Catch card";
    public string Image { get; init; } = "";
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public OcrRegion ToRegion() => new(X, Y, Width, Height);
}

public sealed class RoiDeleteForm
{
    public string Name { get; init; } = "";
}