using System.Runtime.InteropServices;

namespace SoundDeck.CoreProbe;

// Undocumented per-application audio routing (Win10 1803+ / Win11).
// Activated as a WinRT internal class "Windows.Media.Internal.AudioPolicyConfig".
// Only Set/Get/ClearPersistedDefaultAudioEndpoint are real; the preceding vtable
// slots are placeholders to reach the correct index. Same technique as EarTrumpet.

public enum AudioFlow { Render = 0, Capture = 1 }

[ComImport, Guid("AB3D4648-E242-459F-B02F-541C70306324"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioPolicyConfig
{
    // IInspectable slots (GetIids, GetRuntimeClassName, GetTrustLevel) — .NET Core
    // won't add these automatically, so declare them as placeholders.
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

    [DllImport("combase.dll")]
    static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string src, int length, out nint hstring);
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

    // Endpoint id must be wrapped into the SWD/MMDEVAPI form the API expects.
    static string FormatEndpoint(string endpointId, AudioFlow flow)
    {
        string iface = flow == AudioFlow.Render
            ? "{e6327cad-dcec-4949-ae8a-991e976a79d2}"
            : "{2eef81be-33fa-4800-9670-1cd474972c3f}";
        return $@"\\?\SWD#MMDEVAPI#{endpointId}#{iface}";
    }

    static string ReadHString(nint h)
    {
        if (h == 0) return "";
        nint buf = WindowsGetStringRawBuffer(h, out uint len);
        return len == 0 ? "" : Marshal.PtrToStringUni(buf, (int)len);
    }

    // Non-destructive: reads the currently persisted output route for a process.
    public static (int hr, string route) GetRoute(uint pid, AudioFlow flow = AudioFlow.Render)
    {
        var cfg = GetFactory();
        try
        {
            int hr = cfg.GetPersistedDefaultAudioEndpoint(pid, flow, ERole.Multimedia, out nint h);
            string route = hr == 0 ? ReadHString(h) : "";
            if (h != 0) WindowsDeleteString(h);
            return (hr, string.IsNullOrEmpty(route) ? "(follows system default)" : route);
        }
        finally { Marshal.ReleaseComObject(cfg); }
    }

    // Routes a process to a specific endpoint. Pass empty endpointId to reset to default.
    public static int SetRoute(uint pid, string endpointId, AudioFlow flow = AudioFlow.Render)
    {
        var cfg = GetFactory();
        try
        {
            string formatted = string.IsNullOrEmpty(endpointId) ? "" : FormatEndpoint(endpointId, flow);
            WindowsCreateString(formatted, formatted.Length, out nint h);
            try
            {
                int hr = cfg.SetPersistedDefaultAudioEndpoint(pid, flow, ERole.Multimedia, h);
                if (hr == 0)
                    hr = cfg.SetPersistedDefaultAudioEndpoint(pid, flow, ERole.Console, h);
                return hr;
            }
            finally { WindowsDeleteString(h); }
        }
        finally { Marshal.ReleaseComObject(cfg); }
    }
}
