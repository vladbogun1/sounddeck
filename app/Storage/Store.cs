using System.IO;
using System.Text.Json;
using SoundDeck.Model;

namespace SoundDeck.Storage;

// Persists profiles + settings as JSON in %AppData%\SoundDeck.
public class Store
{
    static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoundDeck");
    static readonly string ProfilesPath = Path.Combine(Dir, "profiles.json");
    static readonly string SettingsPath = Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public List<ProfileDto> Profiles { get; private set; } = new();
    public SettingsDto Settings { get; private set; } = new();

    public Store() { Load(); }

    void Load()
    {
        try { Directory.CreateDirectory(Dir); } catch { }
        try
        {
            if (File.Exists(ProfilesPath))
                Profiles = JsonSerializer.Deserialize<List<ProfileDto>>(File.ReadAllText(ProfilesPath)) ?? new();
        }
        catch { Profiles = new(); }
        try
        {
            if (File.Exists(SettingsPath))
                Settings = JsonSerializer.Deserialize<SettingsDto>(File.ReadAllText(SettingsPath)) ?? new();
        }
        catch { Settings = new(); }
    }

    public void SaveProfiles() => WriteAtomic(ProfilesPath, JsonSerializer.Serialize(Profiles, Opts));
    public void SaveSettings() => WriteAtomic(SettingsPath, JsonSerializer.Serialize(Settings, Opts));

    // Write-then-rename so a crash mid-save never leaves a truncated JSON file
    // (which Load would silently replace with defaults).
    static void WriteAtomic(string path, string json)
    {
        try
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) { App.Log("SAVE " + path + ": " + ex.Message); }
    }
}
