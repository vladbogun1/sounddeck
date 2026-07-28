using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using SoundDeck.CoreProbe;

// SoundDeck M0 core probe.
// Commands:
//   (none) | list      -> list output devices + per-app sessions
//   selftest           -> re-apply the CURRENT default (non-destructive IPolicyConfig test)
//   setdefault <index> -> set the device at <index> (from the list) as default for all roles

string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

var enumerator = new MMDeviceEnumerator();

List<MMDevice> ListRenderDevices() =>
    enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();

MMDevice? CurrentDefault()
{
    try { return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); }
    catch { return null; }
}

void PrintDevices()
{
    var devices = ListRenderDevices();
    var def = CurrentDefault();
    Console.WriteLine($"\n=== OUTPUT DEVICES ({devices.Count}) ===");
    for (int i = 0; i < devices.Count; i++)
    {
        var d = devices[i];
        bool isDefault = def != null && d.ID == def.ID;
        int vol = (int)Math.Round(d.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
        string mark = isDefault ? "  <-- DEFAULT" : "";
        Console.WriteLine($"[{i}] {d.FriendlyName}  ({vol}%){mark}");
        Console.WriteLine($"     id: {d.ID}");
    }

    Console.WriteLine("\n=== APP SESSIONS (on default device) ===");
    if (def == null) { Console.WriteLine("  (no default device)"); return; }
    var mgr = def.AudioSessionManager;
    var sessions = mgr.Sessions;
    for (int i = 0; i < sessions.Count; i++)
    {
        var s = sessions[i];
        if (s.State == AudioSessionState.AudioSessionStateExpired) continue;
        uint pid = s.GetProcessID;
        string name = SafeProcName(pid, s.DisplayName);
        int vol = (int)Math.Round(s.SimpleAudioVolume.Volume * 100);
        Console.WriteLine($"  - {name}  (pid {pid}, {vol}%{(s.SimpleAudioVolume.Mute ? ", muted" : "")})");
    }
}

string SafeProcName(uint pid, string fallback)
{
    try
    {
        if (pid == 0) return string.IsNullOrWhiteSpace(fallback) ? "System sounds" : fallback;
        var p = System.Diagnostics.Process.GetProcessById((int)pid);
        return p.ProcessName;
    }
    catch { return string.IsNullOrWhiteSpace(fallback) ? $"pid {pid}" : fallback; }
}

switch (cmd)
{
    case "list":
        PrintDevices();
        break;

    case "selftest":
    {
        var def = CurrentDefault();
        if (def == null) { Console.WriteLine("No default device to test with."); break; }
        Console.WriteLine($"Non-destructive test: re-applying current default '{def.FriendlyName}'...");
        int hr = PolicyConfig.SetDefaultForAllRoles(def.ID);
        Console.WriteLine(hr == 0
            ? "SUCCESS: IPolicyConfig.SetDefaultEndpoint returned S_OK. The set-default hack works on this machine."
            : $"FAILED: HRESULT 0x{hr:X8}");
        break;
    }

    case "setdefault":
    {
        if (args.Length < 2 || !int.TryParse(args[1], out int idx))
        {
            Console.WriteLine("Usage: setdefault <index>");
            break;
        }
        var devices = ListRenderDevices();
        if (idx < 0 || idx >= devices.Count) { Console.WriteLine("Index out of range."); break; }
        var target = devices[idx];
        Console.WriteLine($"Setting default -> '{target.FriendlyName}'...");
        int hr = PolicyConfig.SetDefaultForAllRoles(target.ID);
        Console.WriteLine(hr == 0 ? "SUCCESS." : $"FAILED: HRESULT 0x{hr:X8}");
        PrintDevices();
        break;
    }

    case "getroute":
    {
        if (args.Length < 2 || !uint.TryParse(args[1], out uint pid))
        {
            Console.WriteLine("Usage: getroute <pid>");
            break;
        }
        Console.WriteLine($"Reading persisted output route for pid {pid} (non-destructive)...");
        var (hr, route) = AppRouter.GetRoute(pid);
        Console.WriteLine(hr == 0
            ? $"SUCCESS: IAudioPolicyConfig works. Route = {route}"
            : $"FAILED: HRESULT 0x{hr:X8}");
        break;
    }

    case "setroute":
    {
        if (args.Length < 3 || !uint.TryParse(args[1], out uint pid) || !int.TryParse(args[2], out int di))
        {
            Console.WriteLine("Usage: setroute <pid> <deviceIndex>");
            break;
        }
        var devices = ListRenderDevices();
        if (di < 0 || di >= devices.Count) { Console.WriteLine("Device index out of range."); break; }
        var target = devices[di];
        Console.WriteLine($"Routing pid {pid} -> '{target.FriendlyName}'...");
        int hr = AppRouter.SetRoute(pid, target.ID);
        Console.WriteLine(hr == 0 ? "SUCCESS: write path works." : $"FAILED: HRESULT 0x{hr:X8}");
        var (ghr, route) = AppRouter.GetRoute(pid);
        if (ghr == 0) Console.WriteLine($"Verify route = {route}");
        break;
    }

    default:
        Console.WriteLine("Unknown command. Use: list | selftest | setdefault <index> | getroute <pid> | setroute <pid> <deviceIndex>");
        break;
}
