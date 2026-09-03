using System.Text.Json;
using System.Text.Json.Serialization;
using PlaySphere.Application.Preferences;

namespace PlaySphere.Infrastructure.Preferences;

/// <summary>
/// JSON-file <see cref="IPreferencesStore"/> stored under the app's per-user LocalApplicationData
/// directory (unpackaged app per ADR-0001). Defensive: a missing, empty or corrupt file is read as
/// "no preferences" rather than throwing. Writes are persisted immediately via a temp-file rename so
/// a crash mid-write can't corrupt the existing file.
/// </summary>
public sealed class JsonPreferencesStore : IPreferencesStore
{
    private readonly string _filePath;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public JsonPreferencesStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    public IReadOnlyList<string> GetPreferredAudioLanguages()
    {
        lock (_gate)
        {
            var dto = Load();
            return dto.PreferredAudioLanguages ?? [];
        }
    }

    public void SetPreferredAudioLanguages(IReadOnlyList<string> languages)
    {
        ArgumentNullException.ThrowIfNull(languages);
        lock (_gate)
        {
            var dto = new PreferencesDto { PreferredAudioLanguages = languages.ToList() };
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(dto, JsonOptions));
            File.Move(tmp, _filePath, overwrite: true);
        }
    }

    private PreferencesDto Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new PreferencesDto();
            }

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<PreferencesDto>(json, JsonOptions) ?? new PreferencesDto();
        }
        catch (Exception)
        {
            // Corrupt/unreadable preferences are never fatal — treat as empty.
            return new PreferencesDto();
        }
    }

    private sealed class PreferencesDto
    {
        [JsonPropertyName("preferredAudioLanguages")]
        public List<string>? PreferredAudioLanguages { get; set; }
    }
}
