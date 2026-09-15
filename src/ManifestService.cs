using System.IO;
using System.Text.Json;

namespace IntuneProgressTracker;

public sealed class ManifestService
{
    private readonly string _baseDirectory;
    private readonly JsonSerializerOptions _options = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public ManifestService(string baseDirectory) => _baseDirectory = baseDirectory;

    public TrackerConfig LoadConfig()
    {
        var path = Path.Combine(_baseDirectory, "IntuneProgressTracker.json");
        try { return JsonSerializer.Deserialize<TrackerConfig>(File.ReadAllText(path), _options) ?? new(); }
        catch { return new(); }
    }

    public ManifestRoot LoadManifest()
    {
        var path = Path.Combine(_baseDirectory, "RequiredWorkloads.json");
        try { return JsonSerializer.Deserialize<ManifestRoot>(File.ReadAllText(path), _options) ?? new(); }
        catch { return new(); }
    }
}
