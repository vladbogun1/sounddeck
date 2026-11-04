using System.Drawing;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace SoundDeck;

public partial class App : System.Windows.Application
{
    static System.Threading.Mutex? _mutex;
    System.Threading.EventWaitHandle? _showEvent;
    const string ShowEventName = "SoundDeck_ShowWindow";
    WinForms.NotifyIcon? _tray;
    MainWindow? _window;
    AppController? _controller;

    static readonly string LogPath =
        System.IO.Path.Combine(AppContext.BaseDirectory, "boot.log");

    internal static void Log(string s) { try { System.IO.File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss") + " " + s + "\n"); } catch { } }

    protected override void OnStartup(StartupEventArgs e)
    {
        Log("startup begin");
        base.OnStartup(e);

        DispatcherUnhandledException += (_, ev) => { Log("DISPATCHER: " + ev.Exception); ev.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => Log("DOMAIN: " + ev.ExceptionObject);

        try
        {
            if (Array.Exists(e.Args, a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
            {
                Update.SelfInstall.Uninstall();
                Shutdown();
                return;
            }

            // After a self-update or self-install: wait for the previous process to
            // exit (it holds the single-instance mutex), then drop its leftovers.
            Update.Updater.FinishPendingUpdate(e.Args);

            _mutex = new System.Threading.Mutex(true, "SoundDeck_SingleInstance", out bool isNew);
            if (!isNew)
            {
                // Already running: ask that instance to show its window, then exit.
                try
                {
                    if (System.Threading.EventWaitHandle.TryOpenExisting(ShowEventName, out var ev))
                    {
                        ev.Set();
                        ev.Dispose();
                    }
                }
                catch { }
                Log("second instance -> signaled show, exiting");
                Shutdown();
                return;
            }

            // First run from Downloads etc.: copy into the install folder and restart there.
            if (Update.SelfInstall.InstallAndRelaunchIfNeeded(e.Args))
            {
                Shutdown();
                return;
            }

            _showEvent = new System.Threading.EventWaitHandle(false,
                System.Threading.EventResetMode.AutoReset, ShowEventName);
            StartShowListener();

            Log("creating controller");
            _controller = new AppController();
            Log("creating window");
            _window = new MainWindow(_controller);
            WireUpdates();

            _tray = new WinForms.NotifyIcon
            {
                Icon = BuildTrayIcon(),
                Visible = true,
                Text = "SoundDeck",
            };
            _tray.MouseClick += (_, me) =>
            {
                if (me.Button == WinForms.MouseButtons.Left) _window?.ToggleFromTray();
            };

            var menu = new WinForms.ContextMenuStrip();
            var (open, exit) = TrayLabels(_controller.Language);
            menu.Items.Add(open, null, (_, _) => _window?.ShowFromTray());
            menu.Items.Add(exit, null, (_, _) => Shutdown());
            _tray.ContextMenuStrip = menu;

            // Manual launch opens the window; launch-on-login passes --minimized.
            bool startMinimized = Array.Exists(e.Args, a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));
            if (Environment.GetEnvironmentVariable("SOUNDDECK_TESTMIRROR") == "1") Log(_controller.DebugTestMirror());
            if (!startMinimized) _window.ShowFromTray();
            Log(startMinimized ? "startup done (tray, minimized)" : "startup done (window shown)");
        }
        catch (Exception ex) { Log("STARTUP EX: " + ex); }
    }

    // Tray menu follows the app language ("" = system), like the web UI.
    static (string, string) TrayLabels(string lang)
    {
        if (string.IsNullOrEmpty(lang)) lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return lang switch
        {
            "ru" => ("Открыть", "Выход"),
            "uk" => ("Відкрити", "Вихід"),
            "de" => ("Öffnen", "Beenden"),
            "es" => ("Abrir", "Salir"),
            "fr" => ("Ouvrir", "Quitter"),
            "zh" => ("打开", "退出"),
            "ja" => ("開く", "終了"),
            _ => ("Open", "Exit"),
        };
    }

    bool _installWhenHidden;

    // Installing restarts the app, so an automatic install waits until the window is
    // closed; a click on "install" restarts right away.
    void WireUpdates()
    {
        _controller!.Updates.InstallRequested += now => Dispatcher.BeginInvoke(() =>
        {
            if (now) { Install(startMinimized: false); return; }
            if (_window!.IsVisible) _installWhenHidden = true;
            else Install(startMinimized: true);
        });
        _window!.IsVisibleChanged += (_, _) =>
        {
            if (!_window.IsVisible && _installWhenHidden) Install(startMinimized: true);
        };
    }

    void Install(bool startMinimized)
    {
        _installWhenHidden = false;
        Log("installing update");
        // Release audio while our exe is still in place: after the swap the single-file
        // bundle can no longer lazy-load assemblies (NAudio) needed to shut down.
        var updates = _controller!.Updates;
        _controller.Dispose();
        _controller = null;
        if (!updates.Apply(startMinimized))
        {
            // Swap failed: come back as the same version rather than limp on without audio.
            Log("update failed, restarting current version");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!,
                $"--wait-pid {Environment.ProcessId}" + (startMinimized ? " --minimized" : "")) { UseShellExecute = false });
        }
        Shutdown();
    }

    void StartShowListener()
    {
        var t = new System.Threading.Thread(() =>
        {
            while (true)
            {
                try { if (!_showEvent!.WaitOne()) break; }
                catch { break; }
                try { Dispatcher.Invoke(() => _window?.ShowFromTray()); }
                catch { break; }
            }
        })
        { IsBackground = true, Name = "SoundDeck-ShowListener" };
        t.Start();
    }

    static Icon BuildTrayIcon()
    {
        // Prefer the icon embedded in the exe (works in single-file publish),
        // then a loose appicon.ico (dev), then a drawn glyph.
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var ico = Icon.ExtractAssociatedIcon(exe);
                if (ico != null) return ico;
            }
        }
        catch { }
        try
        {
            var p = System.IO.Path.Combine(AppContext.BaseDirectory, "appicon.ico");
            if (System.IO.File.Exists(p)) return new Icon(p, 32, 32);
        }
        catch { }

        var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var accent = Color.FromArgb(255, 214, 75);
            var ink = Color.FromArgb(20, 17, 11);

            using (var path = RoundedRect(1.5f, 1.5f, 29f, 29f, 8f))
            using (var fill = new SolidBrush(accent))
            using (var border = new Pen(ink, 2.4f))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }

            using var inkBrush = new SolidBrush(ink);
            using var inkPen = new Pen(ink, 2.2f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            // speaker body + cone
            g.FillRectangle(inkBrush, 8, 13, 4, 6);
            g.FillPolygon(inkBrush, new[] { new PointF(12, 16), new PointF(18, 10), new PointF(18, 22) });
            // sound waves
            g.DrawArc(inkPen, 17, 11, 7, 10, -60, 120);
            g.DrawArc(inkPen, 19, 8, 9, 16, -55, 110);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    static System.Drawing.Drawing2D.GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        var p = new System.Drawing.Drawing2D.GraphicsPath();
        p.AddArc(x, y, r, r, 180, 90);
        p.AddArc(x + w - r, y, r, r, 270, 90);
        p.AddArc(x + w - r, y + h - r, r, r, 0, 90);
        p.AddArc(x, y + h - r, r, r, 90, 90);
        p.CloseFigure();
        return p;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _controller?.Dispose();
        try { _showEvent?.Dispose(); } catch { }
        base.OnExit(e);
    }
}
