namespace SoundDeck.Model;

public class DeviceDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Volume { get; set; }
    public bool Muted { get; set; }
    public bool IsDefault { get; set; }
    public string Bus { get; set; } = "";
}

public class SessionDto
{
    public uint Pid { get; set; }
    public string Name { get; set; } = "";
    public string Exe { get; set; } = "";
    public string IconB64 { get; set; } = "";
    public int Volume { get; set; }
    public bool Muted { get; set; }
    public string OutputDeviceId { get; set; } = "";  // "" = follows default
    public bool Mirrored { get; set; }
}

public class ProfileDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string DefaultDeviceId { get; set; } = "";
    public Dictionary<string, string> Routes { get; set; } = new();  // exe -> deviceId
}

public class SettingsDto
{
    public bool AutoHeal { get; set; } = true;
    public string? ActiveProfileId { get; set; }
    public string? MirrorSecondaryDeviceId { get; set; }  // null = mirror off
    public bool Autostart { get; set; }
    public string Theme { get; set; } = "neu";            // "neu" | "glass"
}

public class StateDto
{
    public string Type { get; set; } = "state";
    public List<DeviceDto> Devices { get; set; } = new();
    public List<SessionDto> Sessions { get; set; } = new();
    public List<ProfileDto> Profiles { get; set; } = new();
    public SettingsDto Settings { get; set; } = new();
    public bool MirrorActive { get; set; }
    public string? MirrorTargetDeviceId { get; set; }
}
