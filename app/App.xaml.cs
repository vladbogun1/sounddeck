using System.Drawing;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace SoundDeck;

public partial class App : System.Windows.Application
{
    static System.Threading.Mutex? _mutex;
    WinForms.NotifyIcon? _tray;
    MainWindow? _window;
    AppController? _controller;

    static readonly string LogPath =
        System.IO.Path.Combine(AppContext.BaseDirectory, "boot.log");

    static void Log(string s) { try { System.IO.File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss") + " " + s + "\n"); } catch { } }

    protected override void OnStartup(StartupEventArgs e)
    {
        Log("startup begin");
        base.OnStartup(e);

        DispatcherUnhandledException += (_, ev) => { Log("DISPATCHER: " + ev.Exception); ev.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => Log("DOMAIN: " + ev.ExceptionObject);

        try
        {
            _mutex = new System.Threading.Mutex(true, "SoundDeck_SingleInstance", out bool isNew);
            if (!isNew) { Log("second instance, exiting"); Shutdown(); return; }

            Log("creating controller");
            _controller = new AppController();
            Log("creating window");
            _window = new MainWindow(_controller);

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
            menu.Items.Add("Открыть", null, (_, _) => _window?.ShowFromTray());
            menu.Items.Add("Выход", null, (_, _) => Shutdown());
            _tray.ContextMenuStrip = menu;

            if (Environment.GetEnvironmentVariable("SOUNDDECK_SHOW") == "1") _window.ShowFromTray();
            if (Environment.GetEnvironmentVariable("SOUNDDECK_TESTMIRROR") == "1") Log(_controller.DebugTestMirror());
            Log("startup done (in tray)");
        }
        catch (Exception ex) { Log("STARTUP EX: " + ex); }
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
        base.OnExit(e);
    }
}
