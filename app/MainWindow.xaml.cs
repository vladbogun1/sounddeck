using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using SoundDeck.Model;

namespace SoundDeck;

public partial class MainWindow : Window
{
    readonly AppController _controller;
    bool _ready;

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    long _hiddenTick;

    public MainWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();
        Loaded += OnLoaded;
        if (Environment.GetEnvironmentVariable("SOUNDDECK_SHOW") != "1")
            Deactivated += (_, _) => { _hiddenTick = Environment.TickCount64; Hide(); };
        _controller.StateChanged += () => Dispatcher.Invoke(PushState);
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
      try {
        ApplyRoundedCorners();
        string userData = Path.Combine(Path.GetTempPath(), "SoundDeck.WebView2");
        var env = await CoreWebView2Environment.CreateAsync(null, userData);
        await Web.EnsureCoreWebView2Async(env);

        string root = ResolveWebRoot();
        Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "sounddeck.local", root, CoreWebView2HostResourceAccessKind.Allow);

        Web.CoreWebView2.WebMessageReceived += OnWebMessage;
        Web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        Web.CoreWebView2.Settings.IsStatusBarEnabled = false;

        Web.CoreWebView2.Navigate("https://sounddeck.local/index.html");
        Web.CoreWebView2.NavigationCompleted += (_, _) => { _ready = true; PushState(); };
      }
      catch (Exception ex)
      {
        try { System.IO.File.AppendAllText(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sounddeck-crash.log"),
            DateTime.Now + " ONLOADED: " + ex + "\n"); } catch { }
      }
    }

    // Debug: loose wwwroot next to the exe. Release: extract the embedded zip.
    static string ResolveWebRoot()
    {
#if DEBUG
        return Path.Combine(AppContext.BaseDirectory, "wwwroot");
#else
        var asm = Assembly.GetExecutingAssembly();
        var res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("webassets.zip"));
        if (res == null) return Path.Combine(AppContext.BaseDirectory, "wwwroot");
        string dir = Path.Combine(Path.GetTempPath(), "SoundDeck-web");
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        Directory.CreateDirectory(dir);
        using var s = asm.GetManifestResourceStream(res)!;
        using var zip = new ZipArchive(s, ZipArchiveMode.Read);
        zip.ExtractToDirectory(dir, overwriteFiles: true);
        return dir;
#endif
    }

    void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            string cmd = root.GetProperty("cmd").GetString() ?? "";
            switch (cmd)
            {
                case "getState": PushState(); break;
                case "route":
                    _controller.Route(root.GetProperty("pid").GetUInt32(), Str(root, "deviceId"));
                    break;
                case "setDefault":
                    _controller.SetDefault(Str(root, "deviceId"));
                    break;
                case "setSessionVolume":
                    _controller.SetSessionVolume(root.GetProperty("pid").GetUInt32(), root.GetProperty("value").GetInt32());
                    break;
                case "setSessionMute":
                    _controller.SetSessionMute(root.GetProperty("pid").GetUInt32(), root.GetProperty("value").GetBoolean());
                    break;
                case "setDeviceVolume":
                    _controller.SetDeviceVolume(Str(root, "deviceId"), root.GetProperty("value").GetInt32());
                    break;
                case "setDeviceMute":
                    _controller.SetDeviceMute(Str(root, "deviceId"), root.GetProperty("value").GetBoolean());
                    break;
                case "saveProfile":
                    _controller.SaveProfile(Str(root, "name"));
                    break;
                case "applyProfile":
                    _controller.ApplyProfile(Str(root, "id"));
                    break;
                case "deleteProfile":
                    _controller.DeleteProfile(Str(root, "id"));
                    break;
                case "setAutoHeal":
                    _controller.SetAutoHeal(root.GetProperty("value").GetBoolean());
                    break;
                case "setAutostart":
                    _controller.SetAutostart(root.GetProperty("value").GetBoolean());
                    break;
                case "setTheme":
                    _controller.SetTheme(Str(root, "value"));
                    break;
                case "setGroupApps":
                    _controller.SetGroupApps(root.GetProperty("value").GetBoolean());
                    break;
                case "setLanguage":
                    _controller.SetLanguage(Str(root, "value"));
                    break;
                case "setMirror":
                    _controller.SetMirror(root.TryGetProperty("deviceId", out var d) ? d.GetString() : null);
                    break;
                case "setMirrorVolume":
                    _controller.SetMirrorVolume(root.GetProperty("value").GetInt32());
                    break;
                case "hide": Hide(); break;
            }
            // State-changing commands raise StateChanged, which pushes once.
        }
        catch { }
    }

    static string Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";

    void PushState()
    {
        if (!_ready) return;
        var state = _controller.BuildState();
        Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(state, Json));
    }

    // ---- Tray / flyout behaviour ---------------------------------------

    public void ToggleFromTray()
    {
        if (IsVisible) { _hiddenTick = Environment.TickCount64; Hide(); return; }
        // If we were just dismissed by losing focus to this same tray click,
        // don't immediately reopen — treat the click as "close".
        if (Environment.TickCount64 - _hiddenTick < 350) return;
        ShowFromTray();
    }

    public void ShowFromTray()
    {
        PositionNearTray();
        Show();
        Activate();
        Topmost = true;
        PushState();
        // Start every session with all popovers/menus closed.
        if (_ready) Web.CoreWebView2.PostWebMessageAsJson("{\"type\":\"closePops\"}");
    }

    void PositionNearTray()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width - 12;
        Top = wa.Bottom - Height - 12;
    }

    // ---- Win11 rounded corners -----------------------------------------

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    void ApplyRoundedCorners()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            int pref = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33, ref pref, sizeof(int));
        }
        catch { }
    }
}
