using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using SoundDeck.Model;

namespace SoundDeck.Audio;

// Central audio facade: enumerate, route, volume, and change notifications.
public class AudioService : IMMNotificationClient, IDisposable
{
    readonly MMDeviceEnumerator _enum = new();
    readonly Dictionary<string, string> _iconCache = new();  // exe path -> base64 png
    System.Threading.Timer? _debounce;

    public bool GroupApps { get; set; } = true;  // one card per app vs one per process

    public event Action? Changed;

    public AudioService()
    {
        try { _enum.RegisterEndpointNotificationCallback(this); } catch { }
    }

    // ---- Enumeration ----------------------------------------------------

    List<MMDevice> ActiveRender() =>
        _enum.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();

    MMDevice? DefaultRender()
    {
        try { return _enum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); }
        catch { return null; }
    }

    public List<DeviceDto> GetDevices()
    {
        var def = DefaultRender();
        var list = new List<DeviceDto>();
        foreach (var d in ActiveRender())
        {
            list.Add(new DeviceDto
            {
                Id = d.ID,
                Name = d.FriendlyName,
                Volume = (int)Math.Round(d.AudioEndpointVolume.MasterVolumeLevelScalar * 100),
                Muted = d.AudioEndpointVolume.Mute,
                IsDefault = def != null && d.ID == def.ID,
                Bus = GuessBus(d.FriendlyName),
            });
        }
        return list.OrderByDescending(d => d.IsDefault).ToList();  // default first
    }

    static string GuessBus(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("hdmi") || n.Contains("nvidia") || n.Contains("tv")) return "HDMI";
        if (n.Contains("bluetooth") || n.Contains("bt")) return "BT";
        if (n.Contains("usb") || n.Contains("headset") || n.Contains("fifine")) return "USB";
        return "";
    }

    public List<SessionDto> GetSessions()
    {
        uint self = (uint)Environment.ProcessId;
        // Group by executable: Chromium-style apps (Spotify, Chrome, Discord) run
        // several processes each with its own session — show one card per app.
        var result = new Dictionary<string, SessionDto>();
        foreach (var dev in ActiveRender())
        {
            SessionCollection sessions;
            try { sessions = dev.AudioSessionManager.Sessions; }
            catch { continue; }
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                if (s.State == AudioSessionState.AudioSessionStateExpired) continue;
                uint pid = s.GetProcessID;
                if (pid == self) continue;              // hide our own mirror playback session

                string exe = ExePath(pid);
                string key = (GroupApps && !string.IsNullOrEmpty(exe))
                    ? exe.ToLowerInvariant()
                    : "pid:" + pid;
                if (result.ContainsKey(key)) continue;

                var dto = new SessionDto
                {
                    Pid = pid,
                    Exe = exe,
                    Name = FriendlyName(pid, exe),
                    IconB64 = IconFor(exe),
                    Volume = (int)Math.Round(s.SimpleAudioVolume.Volume * 100),
                    Muted = s.SimpleAudioVolume.Mute,
                    OutputDeviceId = pid == 0 ? "" : AppRouter.GetRouteEndpoint(pid),
                };
                result[key] = dto;
            }
        }
        return result.Values.OrderBy(v => v.Name).ToList();
    }

    // Cross-bitness process path (MainModule fails for 32-bit targets like Steam).
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    static extern nint OpenProcess(int access, bool inherit, int pid);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern bool QueryFullProcessImageNameW(nint h, int flags, System.Text.StringBuilder buf, ref int size);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool CloseHandle(nint h);

    static string ExePath(uint pid)
    {
        if (pid == 0) return "";
        nint h = OpenProcess(0x1000 /*QUERY_LIMITED_INFORMATION*/, false, (int)pid);
        if (h == 0) return "";
        try
        {
            var sb = new System.Text.StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString() : "";
        }
        finally { CloseHandle(h); }
    }

    static string FriendlyName(uint pid, string exe)
    {
        if (pid == 0) return "Системные звуки";
        if (!string.IsNullOrEmpty(exe))
        {
            try
            {
                var fi = FileVersionInfo.GetVersionInfo(exe);
                if (!string.IsNullOrWhiteSpace(fi.FileDescription)) return fi.FileDescription.Trim();
            }
            catch { }
            return Path.GetFileNameWithoutExtension(exe);
        }
        try { return Process.GetProcessById((int)pid).ProcessName; } catch { }
        return $"pid {pid}";
    }

    string IconFor(string exe)
    {
        if (string.IsNullOrEmpty(exe)) return "";
        if (_iconCache.TryGetValue(exe, out var cached)) return cached;
        string b64 = "";
        try
        {
            using var ico = System.Drawing.Icon.ExtractAssociatedIcon(exe);
            if (ico != null)
            {
                using var bmp = ico.ToBitmap();
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                b64 = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
            }
        }
        catch { }
        _iconCache[exe] = b64;
        return b64;
    }

    // ---- Actions --------------------------------------------------------

    public int SetDefaultDevice(string deviceId) => PolicyConfig.SetDefaultForAllRoles(deviceId);

    // Route every process that belongs to the same app (same executable).
    public int RouteApp(uint pid, string deviceId)
    {
        int hr = 0;
        foreach (var p in PidsOfApp(pid)) hr = AppRouter.SetRoute(p, deviceId);
        return hr;
    }

    // All process ids sharing the executable of the given pid (across all devices).
    IEnumerable<uint> PidsOfApp(uint pid)
    {
        string exe = ExePath(pid);
        if (!GroupApps || string.IsNullOrEmpty(exe)) return new[] { pid };
        var set = new HashSet<uint>();
        foreach (var dev in ActiveRender())
        {
            SessionCollection ss;
            try { ss = dev.AudioSessionManager.Sessions; }
            catch { continue; }
            for (int i = 0; i < ss.Count; i++)
            {
                uint sp = ss[i].GetProcessID;
                if (sp != 0 && ExePath(sp) == exe) set.Add(sp);
            }
        }
        if (set.Count == 0) set.Add(pid);
        return set;
    }

    public void SetDeviceVolume(string deviceId, int percent)
    {
        var d = ActiveRender().FirstOrDefault(x => x.ID == deviceId);
        if (d != null) d.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(percent, 0, 100) / 100f;
    }

    public void SetDeviceMute(string deviceId, bool mute)
    {
        var d = ActiveRender().FirstOrDefault(x => x.ID == deviceId);
        if (d != null) d.AudioEndpointVolume.Mute = mute;
    }

    public void SetSessionVolume(uint pid, int percent)
    {
        WithSession(pid, s => s.SimpleAudioVolume.Volume = Math.Clamp(percent, 0, 100) / 100f);
    }

    public void SetSessionMute(uint pid, bool mute)
    {
        WithSession(pid, s => s.SimpleAudioVolume.Mute = mute);
    }

    // Apply to every session of the same app (same executable), not just one pid.
    void WithSession(uint pid, Action<AudioSessionControl> action)
    {
        string exe = ExePath(pid);
        bool byExe = GroupApps && !string.IsNullOrEmpty(exe);
        foreach (var dev in ActiveRender())
        {
            SessionCollection sessions;
            try { sessions = dev.AudioSessionManager.Sessions; }
            catch { continue; }
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                uint sp = s.GetProcessID;
                bool match = byExe ? ExePath(sp) == exe : sp == pid;
                if (match) { try { action(s); } catch { } }
            }
        }
    }

    public string? DefaultDeviceId => DefaultRender()?.ID;

    public MMDevice? GetDevice(string id) => ActiveRender().FirstOrDefault(x => x.ID == id);

    // ---- Change notifications (debounced) -------------------------------

    void Notify()
    {
        _debounce?.Dispose();
        _debounce = new System.Threading.Timer(_ => Changed?.Invoke(), null, 300, System.Threading.Timeout.Infinite);
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => Notify();
    public void OnDeviceAdded(string pwstrDeviceId) => Notify();
    public void OnDeviceRemoved(string deviceId) => Notify();
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => Notify();
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        try { _enum.UnregisterEndpointNotificationCallback(this); } catch { }
        _debounce?.Dispose();
    }
}
