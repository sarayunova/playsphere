using PlaySphere.Infrastructure.Preferences;
using Xunit;

namespace PlaySphere.IntegrationTests;

/// <summary>
/// Tests for <see cref="JsonPreferencesStore"/> against a real temp-file JSON store: default-empty,
/// round-trip persistence, corrupt-file resilience and rewrite.
/// </summary>
public sealed class PreferencesStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PlaySpherePrefs_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private string FilePath => Path.Combine(_dir, "preferences.json");

    [Fact]
    public void New_store_returns_empty_preferences()
    {
        var store = new JsonPreferencesStore(FilePath);
        Assert.Empty(store.GetPreferredAudioLanguages());
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void Set_then_get_round_trips_languages_in_order()
    {
        var store = new JsonPreferencesStore(FilePath);
        store.SetPreferredAudioLanguages(["en", "fr"]);
        Assert.Equal(["en", "fr"], store.GetPreferredAudioLanguages());
        Assert.True(File.Exists(FilePath));
    }

    [Fact]
    public void Get_survives_a_corrupt_file_as_empty()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ not valid json !!");
        var store = new JsonPreferencesStore(FilePath);
        Assert.Empty(store.GetPreferredAudioLanguages());
    }

    [Fact]
    public void Set_overwrites_previous_languages()
    {
        var store = new JsonPreferencesStore(FilePath);
        store.SetPreferredAudioLanguages(["en"]);
        store.SetPreferredAudioLanguages(["de", "ja"]);
        Assert.Equal(["de", "ja"], store.GetPreferredAudioLanguages());
    }
}
