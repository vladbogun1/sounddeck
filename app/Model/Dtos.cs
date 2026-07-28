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
    public bool IsMirror { get; set; }                 // synthetic card for the mirror stream
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
    public string Theme { get; set; } = "neu";            // "neu" | "glass" | ...
    public bool GroupApps { get; set; } = true;           // merge multi-process apps into one card
    public string Language { get; set; } = "";            // "" = auto (system), else e.g. "en","ru"
    public double MirrorVolume { get; set; } = 1.0;       // loudness of the mirrored stream (0..1)
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
