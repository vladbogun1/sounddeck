using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace SoundDeck.Update;

// The exe installs itself: launched from anywhere (Downloads, a USB stick), it copies
// itself to %LocalAppData%\Programs\SoundDeck, adds a Start-menu shortcut and an
// "Apps & features" entry, moves launch-on-login to the new path and restarts from
// there. Per-user folder → no admin rights, and the Updater can swap the exe in place.
//
// Opt out with --portable or a "portable" file next to the exe.
public static class SelfInstall
{
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SoundDeck";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SoundDeck");
    public static string InstalledExe => Path.Combine(Dir, "SoundDeck.exe");
    static string Shortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "SoundDeck.lnk");

    static string CurrentExe => Environment.ProcessPath!;
    public static bool IsInstalledCopy =>
        string.Equals(Path.GetFullPath(CurrentExe), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    // Returns true when a copy in the install folder was started and this process
    // must exit. Call only from the single running instance.
    public static bool InstallAndRelaunchIfNeeded(string[] args)
    {
#if DEBUG
        return false;
#else
        if (IsPortable(args)) return false;
        if (IsInstalledCopy) { Repair(); return false; }

        try
        {
            Directory.CreateDirectory(Dir);
            // Never downgrade: if a newer build is already installed, just start it.
            if (!File.Exists(InstalledExe) || FileVersion(InstalledExe) <= FileVersion(CurrentExe))
            {
                var tmp = InstalledExe + ".new";
                File.Copy(CurrentExe, tmp, overwrite: true);
                File.Move(tmp, InstalledExe, overwrite: true);
            }

            var rest = args.Where(a => !a.Equals("--updated", StringComparison.OrdinalIgnoreCase));
            var argLine = string.Join(" ", rest.Append("--wait-pid").Append(Environment.ProcessId.ToString()));
            Process.Start(new ProcessStartInfo(InstalledExe, argLine) { UseShellExecute = false, WorkingDirectory = Dir });
            App.Log("self-install: relaunching from " + InstalledExe);
            return true;
        }
        catch (Exception ex)
        {
            App.Log("self-install failed, running in place: " + ex.Message);
            return false;
        }
#endif
    }

    static bool IsPortable(string[] args) =>
        args.Any(a => a.Equals("--portable", StringComparison.OrdinalIgnoreCase))
        || File.Exists(Path.Combine(Path.GetDirectoryName(CurrentExe)!, "portable"));

    static Version FileVersion(string path)
    {
        try { return Version.Parse(FileVersionInfo.GetVersionInfo(path).FileVersion ?? "0.0"); }
        catch { return new Version(0, 0); }
    }

    // Runs on every start of the installed copy: keeps the shortcut, the uninstall
    // entry (version!) and the autostart path pointing at this exe.
    static void Repair()
    {
        try { EnsureShortcut(); } catch (Exception ex) { App.Log("shortcut: " + ex.Message); }
        try { WriteUninstallEntry(); } catch (Exception ex) { App.Log("uninstall entry: " + ex.Message); }
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (k?.GetValue("SoundDeck") is string v && !v.Contains(InstalledExe, StringComparison.OrdinalIgnoreCase))
                k.SetValue("SoundDeck", $"\"{InstalledExe}\" --minimized");
        }
        catch { }
    }

    static void EnsureShortcut()
    {
        if (File.Exists(Shortcut)) return;
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return;
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic lnk = shell.CreateShortcut(Shortcut);
        lnk.TargetPath = InstalledExe;
        lnk.WorkingDirectory = Dir;
        lnk.IconLocation = InstalledExe + ",0";
        lnk.Description = "SoundDeck";
        lnk.Save();
    }

    static void WriteUninstallEntry()
    {
        using var k = Registry.CurrentUser.CreateSubKey(UninstallKey);
        var ver = FileVersionInfo.GetVersionInfo(InstalledExe);
        k.SetValue("DisplayName", "SoundDeck");
        k.SetValue("DisplayVersion", $"{ver.FileMajorPart}.{ver.FileMinorPart}.{ver.FileBuildPart}");
        k.SetValue("Publisher", "vladbogun1");
        k.SetValue("DisplayIcon", InstalledExe);
        k.SetValue("InstallLocation", Dir);
        k.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
        k.SetValue("URLInfoAbout", "https://github.com/vladbogun1/sounddeck");
        k.SetValue("NoModify", 1, RegistryValueKind.DWord);
        k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        k.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
    }

    // "--uninstall" from Apps & features. Settings in %AppData%\SoundDeck are kept so a
    // reinstall comes back with the same profiles.
    public static void Uninstall()
    {
        var answer = System.Windows.MessageBox.Show("Remove SoundDeck from this computer?", "SoundDeck",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.Yes) return;

        foreach (var p in Process.GetProcessesByName("SoundDeck"))
        {
            try { if (p.Id != Environment.ProcessId) { p.Kill(); p.WaitForExit(5000); } } catch { }
            finally { p.Dispose(); }
        }
        try { using var k = Registry.CurrentUser.OpenSubKey(RunKey, true); k?.DeleteValue("SoundDeck", false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
        try { if (File.Exists(Shortcut)) File.Delete(Shortcut); } catch { }

        // We are running from the folder being removed: let cmd delete it after we exit.
        Process.Start(new ProcessStartInfo("cmd.exe",
            $"/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"{Dir}\"")
        { CreateNoWindow = true, UseShellExecute = false });
    }
}
