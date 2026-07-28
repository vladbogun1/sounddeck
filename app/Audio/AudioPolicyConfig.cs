using System.Runtime.InteropServices;

namespace SoundDeck.Audio;

// Undocumented per-application audio routing (validated in M0).
// WinRT class, but .NET Core can't marshal IInspectable, so we declare it as
// IUnknown and add the 3 IInspectable slots manually.

public enum AudioFlow { Render = 0, Capture = 1 }

[ComImport, Guid("AB3D4648-E242-459F-B02F-541C70306324"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioPolicyConfig
{
    [PreserveSig] int GetIids(out int count, out nint iids);
    [PreserveSig] int GetRuntimeClassName(out nint className);
    [PreserveSig] int GetTrustLevel(out int trustLevel);

    [PreserveSig] int _00(); [PreserveSig] int _01(); [PreserveSig] int _02();
    [PreserveSig] int _03(); [PreserveSig] int _04(); [PreserveSig] int _05();
    [PreserveSig] int _06(); [PreserveSig] int _07(); [PreserveSig] int _08();
    [PreserveSig] int _09(); [PreserveSig] int _10(); [PreserveSig] int _11();
    [PreserveSig] int _12(); [PreserveSig] int _13(); [PreserveSig] int _14();
    [PreserveSig] int _15(); [PreserveSig] int _16(); [PreserveSig] int _17();
    [PreserveSig] int _18();

    [PreserveSig] int SetPersistedDefaultAudioEndpoint(
        uint processId, AudioFlow flow, ERole role, nint deviceIdHString);
    [PreserveSig] int GetPersistedDefaultAudioEndpoint(
        uint processId, AudioFlow flow, ERole role, out nint deviceIdHString);
    [PreserveSig] int ClearAllPersistedApplicationDefaultEndpoints();
}

public static class AppRouter
{
    const string ClassName = "Windows.Media.Internal.AudioPolicyConfig";
    static readonly Guid IID = new("AB3D4648-E242-459F-B02F-541C70306324");
    const string RenderIface = "{e6327cad-dcec-4949-ae8a-991e976a79d2}";

    [DllImport("combase.dll")]
    static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string src, int length, out nint hstring);
    [DllImport("combase.dll")]
    static extern int WindowsDeleteString(nint hstring);
    [DllImport("combase.dll")]
    static extern nint WindowsGetStringRawBuffer(nint hstring, out uint length);
    [DllImport("combase.dll")]
    static extern int RoGetActivationFactory(nint classId, ref Guid iid, out nint factory);

    static IAudioPolicyConfig GetFactory()
    {
        WindowsCreateString(ClassName, ClassName.Length, out nint classId);
        try
        {
            var iid = IID;
            int hr = RoGetActivationFactory(classId, ref iid, out nint factoryPtr);
            if (hr != 0) throw new COMException("RoGetActivationFactory failed", hr);
            var obj = Marshal.GetObjectForIUnknown(factoryPtr);
            Marshal.Release(factoryPtr);
            return (IAudioPolicyConfig)obj;
        }
        finally { WindowsDeleteString(classId); }
    }

    static string FormatEndpoint(string endpointId) => $@"\\?\SWD#MMDEVAPI#{endpointId}#{RenderIface}";

    // Extracts the raw endpoint id back out of a formatted SWD route string.
    public static string ParseEndpoint(string route)
    {
        if (string.IsNullOrEmpty(route)) return "";
        const string marker = "MMDEVAPI#";
        int i = route.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return "";
        int start = i + marker.Length;
        int end = route.IndexOf('#', start);
        return end < 0 ? route[start..] : route[start..end];
    }

    static string ReadHString(nint h)
    {
        if (h == 0) return "";
        nint buf = WindowsGetStringRawBuffer(h, out uint len);
        return len == 0 ? "" : Marshal.PtrToStringUni(buf, (int)len) ?? "";
    }

    // Returns the raw endpoint id the process is routed to, or "" if it follows default.
    public static string GetRouteEndpoint(uint pid, AudioFlow flow = AudioFlow.Render)
    {
        var cfg = GetFactory();
        try
        {
            int hr = cfg.GetPersistedDefaultAudioEndpoint(pid, flow, ERole.Multimedia, out nint h);
            string route = hr == 0 ? ReadHString(h) : "";
            if (h != 0) WindowsDeleteString(h);
            return ParseEndpoint(route);
        }
        catch { return ""; }
        finally { Marshal.ReleaseComObject(cfg); }
    }

    // Routes a process to an endpoint. Empty endpointId resets it to the system default.
    public static int SetRoute(uint pid, string endpointId, AudioFlow flow = AudioFlow.Render)
    {
        var cfg = GetFactory();
        try
        {
            string formatted = string.IsNullOrEmpty(endpointId) ? "" : FormatEndpoint(endpointId);
            WindowsCreateString(formatted, formatted.Length, out nint h);
            try
            {
                int hr = cfg.SetPersistedDefaultAudioEndpoint(pid, flow, ERole.Multimedia, h);
                if (hr == 0) hr = cfg.SetPersistedDefaultAudioEndpoint(pid, flow, ERole.Console, h);
                return hr;
            }
            finally { WindowsDeleteString(h); }
        }
        finally { Marshal.ReleaseComObject(cfg); }
    }
}
