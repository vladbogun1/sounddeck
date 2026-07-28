using SoundDeck.Audio;
using SoundDeck.Model;
using SoundDeck.Storage;

namespace SoundDeck;

// Ties the audio service, storage, mirror engine, profiles and auto-heal together.
public class AppController : IDisposable
{
    readonly AudioService _audio = new();
    readonly MirrorEngine _mirror = new();
    readonly Store _store = new();

    public event Action? StateChanged;

    public AppController()
    {
        _audio.GroupApps = _store.Settings.GroupApps;
        _audio.Changed += OnAudioChanged;
        // Restore mirror if it was on.
        if (_store.Settings.MirrorSecondaryDeviceId is string sec)
            StartMirror(sec);
    }

    void OnAudioChanged()
    {
        if (_store.Settings.AutoHeal) TryHeal();
        StateChanged?.Invoke();
    }

    // ---- State ----------------------------------------------------------

    public StateDto BuildState()
    {
        _audio.GroupApps = _store.Settings.GroupApps;
        var devices = _audio.GetDevices();
        var sessions = _audio.GetSessions();
        var deviceIds = devices.Select(d => d.Id).ToHashSet();
        foreach (var s in sessions)
        {
            // Route endpoint may point to an inactive device; blank it so the card
            // falls into the default column rather than vanishing.
            if (!string.IsNullOrEmpty(s.OutputDeviceId) && !deviceIds.Contains(s.OutputDeviceId))
                s.OutputDeviceId = "";
        }

        // Mirror shows up as its own card on the target device — with the app icon
        // and a volume slider that controls how loud the duplicated stream is.
        if (_mirror.Active && !string.IsNullOrEmpty(_mirror.TargetDeviceId)
            && deviceIds.Contains(_mirror.TargetDeviceId))
        {
            sessions.Add(new SessionDto
            {
                Pid = uint.MaxValue,
                Name = "SoundDeck",
                IconB64 = AppIcon(),
                Volume = (int)Math.Round(_mirror.Volume * 100),
                OutputDeviceId = _mirror.TargetDeviceId,
                IsMirror = true,
            });
        }

        return new StateDto
        {
            Devices = devices,
            Sessions = sessions,
            Profiles = _store.Profiles,
            Settings = _store.Settings,
            MirrorActive = _mirror.Active,
            MirrorTargetDeviceId = _mirror.TargetDeviceId,
        };
    }

    // ---- Commands -------------------------------------------------------

    // Structural changes refresh the board; volume/mute do not (avoids flicker).
    public void SetDefault(string deviceId) { _audio.SetDefaultDevice(deviceId); StateChanged?.Invoke(); }
    public void Route(uint pid, string deviceId) { _audio.RouteApp(pid, deviceId); StateChanged?.Invoke(); }
    public void SetSessionVolume(uint pid, int v) => _audio.SetSessionVolume(pid, v);
    public void SetSessionMute(uint pid, bool m) { _audio.SetSessionMute(pid, m); StateChanged?.Invoke(); }
    public void SetDeviceVolume(string id, int v) => _audio.SetDeviceVolume(id, v);
    public void SetDeviceMute(string id, bool m) { _audio.SetDeviceMute(id, m); StateChanged?.Invoke(); }

    public void SetMirrorVolume(int percent)
    {
        float v = Math.Clamp(percent, 0, 100) / 100f;
        _mirror.Volume = v;
        _store.Settings.MirrorVolume = v;
        _store.SaveSettings();
    }

    // App's own icon (from the exe) as a data URI, for the mirror card. Cached.
    static string? _appIcon;
    static string AppIcon()
    {
        if (_appIcon != null) return _appIcon;
        try
        {
            using var ico = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
            using var bmp = ico!.ToBitmap();
            using var ms = new System.IO.MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            _appIcon = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
        }
        catch { _appIcon = ""; }
        return _appIcon;
    }

    // ---- Profiles -------------------------------------------------------

    public void SaveProfile(string name)
    {
        var routes = new Dictionary<string, string>();
        foreach (var s in _audio.GetSessions())
            if (!string.IsNullOrEmpty(s.Exe) && !string.IsNullOrEmpty(s.OutputDeviceId))
                routes[s.Exe.ToLowerInvariant()] = s.OutputDeviceId;

        var profile = new ProfileDto
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"Профиль {_store.Profiles.Count + 1}" : name,
            DefaultDeviceId = _audio.DefaultDeviceId ?? "",
            Routes = routes,
        };
        _store.Profiles.Add(profile);
        _store.Settings.ActiveProfileId = profile.Id;
        _store.SaveProfiles();
        _store.SaveSettings();
        StateChanged?.Invoke();
    }

    public void ApplyProfile(string id)
    {
        var p = _store.Profiles.FirstOrDefault(x => x.Id == id);
        if (p == null) return;
        _store.Settings.ActiveProfileId = id;
        _store.SaveSettings();
        ApplyProfileInternal(p);
        StateChanged?.Invoke();
    }

    void ApplyProfileInternal(ProfileDto p)
    {
        if (!string.IsNullOrEmpty(p.DefaultDeviceId) && _audio.GetDevice(p.DefaultDeviceId) != null)
            _audio.SetDefaultDevice(p.DefaultDeviceId);
        foreach (var s in _audio.GetSessions())
        {
            if (string.IsNullOrEmpty(s.Exe)) continue;
            if (p.Routes.TryGetValue(s.Exe.ToLowerInvariant(), out var dev)
                && _audio.GetDevice(dev) != null)
                _audio.RouteApp(s.Pid, dev);
        }
    }

    public void DeleteProfile(string id)
    {
        _store.Profiles.RemoveAll(x => x.Id == id);
        if (_store.Settings.ActiveProfileId == id) _store.Settings.ActiveProfileId = null;
        _store.SaveProfiles();
        _store.SaveSettings();
        StateChanged?.Invoke();
    }

    // Re-applies the active profile if its preferred default device is present.
    void TryHeal()
    {
        var id = _store.Settings.ActiveProfileId;
        if (id == null) return;
        var p = _store.Profiles.FirstOrDefault(x => x.Id == id);
        if (p == null) return;
        if (string.IsNullOrEmpty(p.DefaultDeviceId)) return;
        if (_audio.GetDevice(p.DefaultDeviceId) == null) return;   // preferred device not back yet
        if (_audio.DefaultDeviceId == p.DefaultDeviceId) return;   // already correct
        ApplyProfileInternal(p);
    }

    // ---- Settings -------------------------------------------------------

    public void SetAutoHeal(bool on)
    {
        _store.Settings.AutoHeal = on;
        _store.SaveSettings();
        StateChanged?.Invoke();
    }

    static readonly HashSet<string> Themes = new()
    {
        "neu", "glass", "win11", "ios", "material", "paper", "solarized", "dracula",
        "nord", "gruvbox", "monokai", "catppuccin", "rose", "oled", "matrix",
        "forest", "ocean", "sunset", "synthwave", "cyberpunk",
    };
    public void SetTheme(string theme)
    {
        _store.Settings.Theme = Themes.Contains(theme) ? theme : "neu";
        _store.SaveSettings();
        StateChanged?.Invoke();
    }

    public void SetGroupApps(bool on)
    {
        _store.Settings.GroupApps = on;
        _audio.GroupApps = on;
        _store.SaveSettings();
        StateChanged?.Invoke();
    }

    public void SetLanguage(string lang)
    {
        _store.Settings.Language = lang ?? "";
        _store.SaveSettings();
        StateChanged?.Invoke();
    }

    public void SetAutostart(bool on)
    {
        Autostart.Set(on);
        _store.Settings.Autostart = Autostart.IsEnabled();
        _store.SaveSettings();
        StateChanged?.Invoke();
    }

    public void SetMirror(string? secondaryDeviceId)
    {
        if (string.IsNullOrEmpty(secondaryDeviceId)) { _mirror.Stop(); _store.Settings.MirrorSecondaryDeviceId = null; }
        else StartMirror(secondaryDeviceId);
        _store.Settings.MirrorSecondaryDeviceId = _mirror.Active ? secondaryDeviceId : null;
        _store.SaveSettings();
        StateChanged?.Invoke();
    }

    // Debug helper: start mirror to the first non-default device and report result.
    public string DebugTestMirror()
    {
        var target = _audio.GetDevices().FirstOrDefault(d => !d.IsDefault);
        if (target == null) return "no non-default device to mirror to";
        StartMirror(target.Id);
        return _mirror.Active
            ? $"mirror STARTED -> {target.Name}"
            : $"mirror FAILED: {_mirror.LastError}";
    }

    void StartMirror(string secondaryDeviceId)
    {
        var target = _audio.GetDevice(secondaryDeviceId);
        var source = _audio.DefaultDeviceId is string d ? _audio.GetDevice(d) : null;
        if (target != null && source != null && target.ID != source.ID)
        {
            if (_mirror.Start(source, target))
                _mirror.Volume = (float)_store.Settings.MirrorVolume;
        }
    }

    public void Dispose()
    {
        _mirror.Dispose();
        _audio.Dispose();
    }
}
