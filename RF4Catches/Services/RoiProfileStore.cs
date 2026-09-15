using System.Text.Json;

namespace RF4Catches.Services;

public sealed record RoiProfile(string Name, OcrRegion Region, DateTimeOffset SavedAtUtc);

public sealed class RoiProfileStore(IWebHostEnvironment environment)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path = Path.Combine(environment.ContentRootPath, "data", "roi-profiles.json");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<IReadOnlyList<RoiProfile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await ReadAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(string name, OcrRegion region, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80)
            throw new ArgumentException("A region name of up to 80 characters is required.", nameof(name));
        if (region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0 || region.X + region.Width > 1 || region.Y + region.Height > 1)
            throw new ArgumentOutOfRangeException(nameof(region), "The region must fit within the image.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var profiles = (await ReadAsync(cancellationToken)).Where(p => !string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            profiles.Add(new RoiProfile(name.Trim(), region, DateTimeOffset.UtcNow));
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(profiles, JsonOptions), cancellationToken);
        }
        finally { _gate.Release(); }
    }
    
    public async Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await ReadAsync(cancellationToken);
            var filtered = existing
                .Where(p => !string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (filtered.Count == existing.Count) return false;

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(filtered, JsonOptions), cancellationToken);
            return true;
        }
        finally { _gate.Release(); }
    }
    
    private async Task<List<RoiProfile>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return [];
        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<List<RoiProfile>>(stream, JsonOptions, cancellationToken) ?? [];
    }
}
