using System.Runtime.InteropServices;

namespace SoundDeck.Audio;

// Undocumented interface to set the default audio endpoint (validated in M0).

public enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

[ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
internal class CPolicyConfigClient { }

[ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat(nint p0, nint p1);
    [PreserveSig] int GetDeviceFormat(nint p0, int p1, nint p2);
    [PreserveSig] int ResetDeviceFormat(nint p0);
    [PreserveSig] int SetDeviceFormat(nint p0, nint p1, nint p2);
    [PreserveSig] int GetProcessingPeriod(nint p0, int p1, nint p2, nint p3);
    [PreserveSig] int SetProcessingPeriod(nint p0, nint p1);
    [PreserveSig] int GetShareMode(nint p0, nint p1);
    [PreserveSig] int SetShareMode(nint p0, nint p1);
    [PreserveSig] int GetPropertyValue(nint p0, int p1, nint p2, nint p3);
    [PreserveSig] int SetPropertyValue(nint p0, int p1, nint p2, nint p3);
    [PreserveSig] int SetDefaultEndpoint(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    [PreserveSig] int SetEndpointVisibility(nint p0, int p1);
}

public static class PolicyConfig
{
    public static int SetDefaultEndpoint(string deviceId, ERole role)
    {
        var client = (IPolicyConfig)new CPolicyConfigClient();
        try { return client.SetDefaultEndpoint(deviceId, role); }
        finally { Marshal.ReleaseComObject(client); }
    }

    public static int SetDefaultForAllRoles(string deviceId)
    {
        foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
        {
            int hr = SetDefaultEndpoint(deviceId, role);
            if (hr != 0) return hr;
        }
        return 0;
    }
}
