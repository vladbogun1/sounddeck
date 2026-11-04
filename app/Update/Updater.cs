using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using SoundDeck.Model;

namespace SoundDeck.Update;

// Self-update from GitHub Releases.
//
// The release asset is the single-file SoundDeck.exe. Windows lets a running exe be
// renamed (not overwritten), so install = download next to the exe as .new, rename the
// running exe to .old, move .new into place, start it and exit. The new process waits
// for the old pid to die, then deletes .old.
//
// Safety: HTTPS + the asset's sha256 digest from the GitHub API, and if the running exe
// is Authenticode-signed the download must carry the same signer certificate.
public sealed class Updater : IDisposable
{
    const string Repo = "vladbogun1/sounddeck";
    const string AssetName = "SoundDeck.exe";
    static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(20);
    static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);

    readonly HttpClient _http;
    readonly object _gate = new();
    Release? _latest;
    string? _downloadedPath;
    Version? _downloadedVersion;
    bool _busy;

    public UpdateDto Info { get; } = new();
    public event Action? Changed;

    public Updater()
    {
        _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };  // per-request timeouts below
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SoundDeck-Updater");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        Info.CurrentVersion = Format(CurrentVersion);
        Info.CanInstall = CanWriteNextToExe();
    }

    // Test hook: pretend to be an older build to exercise the whole flow.
    static Version CurrentVersion => Normalize(
        Version.TryParse(Environment.GetEnvironmentVariable("SOUNDDECK_FAKE_VERSION"), out var v) ? v
        : Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0));

    // "1.4" and "1.4.0.0" compare equal.
    static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    // ---- check ----------------------------------------------------------

    public async Task CheckAsync()
    {
        if (!Enter()) return;
        try
        {
            Set(UpdateStatus.Checking);
            var rel = await FetchLatestAsync();
            Info.CheckedAt = DateTime.Now;
            if (rel == null || rel.Version <= CurrentVersion)
            {
                _latest = null;
                Info.LatestVersion = rel != null ? Format(rel.Version) : null;
                Set(UpdateStatus.UpToDate);
                return;
            }
            _latest = rel;
            Info.LatestVersion = Format(rel.Version);
            Info.ReleaseUrl = rel.HtmlUrl;
            Info.Notes = rel.Notes;
            Set(HasDownloaded && _downloadedVersion == rel.Version ? UpdateStatus.Ready : UpdateStatus.Available);
        }
        catch (Exception ex) { Fail("check", ex); }
        finally { Exit(); }
    }

    async Task<Release?> FetchLatestAsync()
    {
        string url = $"https://api.github.com/repos/{Repo}/releases/latest";
        // GET is idempotent: retry transient failures twice with a short backoff.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var cts = new CancellationTokenSource(ApiTimeout);
                using var resp = await _http.GetAsync(url, cts.Token);
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;  // no releases yet
                if ((int)resp.StatusCode >= 500 && attempt < 2) { await Task.Delay(2000 * (attempt + 1)); continue; }
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync(cts.Token);
                return Release.Parse(json);
            }
            catch (Exception ex) when (attempt < 2 && ex is HttpRequestException or TaskCanceledException)
            {
                await Task.Delay(2000 * (attempt + 1));
            }
        }
    }

    // ---- download -------------------------------------------------------

    public async Task<bool> DownloadAsync()
    {
        var rel = _latest;
        if (rel == null || rel.Asset == null) return false;
        if (!Info.CanInstall) return false;
        if (!Enter()) return false;
        string target = Environment.ProcessPath! + ".new";
        try
        {
            Set(UpdateStatus.Downloading);
            Info.Progress = 0;
            using (var cts = new CancellationTokenSource(DownloadTimeout))
            using (var resp = await _http.GetAsync(rel.Asset.Url, HttpCompletionOption.ResponseHeadersRead, cts.Token))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? rel.Asset.Size;
                await using var src = await resp.Content.ReadAsStreamAsync(cts.Token);
                await using var dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true);
                var buf = new byte[1 << 16];
                long done = 0; int lastPct = -1; int n;
                while ((n = await src.ReadAsync(buf, cts.Token)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), cts.Token);
                    done += n;
                    int pct = total > 0 ? (int)(done * 100 / total) : 0;
                    if (pct != lastPct && pct % 5 == 0) { lastPct = pct; Info.Progress = pct; Changed?.Invoke(); }
                }
            }

            Verify(target, rel.Asset);
            _downloadedPath = target;
            _downloadedVersion = rel.Version;
            Info.Progress = 100;
            Set(UpdateStatus.Ready);
            return true;
        }
        catch (Exception ex)
        {
            TryDelete(target);
            Fail("download", ex);
            return false;
        }
        finally { Exit(); }
    }

    static void Verify(string path, Asset asset)
    {
        var len = new FileInfo(path).Length;
        if (asset.Size > 0 && len != asset.Size)
            throw new InvalidDataException($"size mismatch: {len} != {asset.Size}");

        if (asset.Sha256 != null)
        {
            using var fs = File.OpenRead(path);
            var hash = Convert.ToHexString(SHA256.HashData(fs));
            if (!hash.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("sha256 mismatch");
        }

        // A signed build only accepts an update signed by the same certificate.
        var mine = SignerThumbprint(Environment.ProcessPath!);
        if (mine != null && SignerThumbprint(path) != mine)
            throw new InvalidDataException("signature does not match the installed build");
    }

    static string? SignerThumbprint(string file)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using var c = new X509Certificate2(X509Certificate.CreateFromSignedFile(file));
#pragma warning restore SYSLIB0057
            return c.Thumbprint;
        }
        catch { return null; }
    }

    // ---- install --------------------------------------------------------

    public bool HasDownloaded => _downloadedPath != null && File.Exists(_downloadedPath);

    // Swaps the exe and starts the new one. Caller must shut the app down on true.
    public bool ApplyAndRestart(bool startMinimized)
    {
        if (!HasDownloaded) return false;
        string exe = Environment.ProcessPath!;
        string old = exe + ".old";
        try
        {
            TryDelete(old);
            File.Move(exe, old);
            try { File.Move(_downloadedPath!, exe); }
            catch { File.Move(old, exe); throw; }

            var args = $"--updated --wait-pid {Environment.ProcessId}" + (startMinimized ? " --minimized" : "");
            Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
            });
            return true;
        }
        catch (Exception ex)
        {
            Fail("install", ex);
            return false;
        }
    }

    // Called first thing on startup: finish a swap started by the previous version.
    public static void FinishPendingUpdate(string[] args)
    {
        int i = Array.FindIndex(args, a => a.Equals("--wait-pid", StringComparison.OrdinalIgnoreCase));
        if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int pid))
        {
            try { using var p = Process.GetProcessById(pid); p.WaitForExit(15000); } catch { }
        }
        var exe = Environment.ProcessPath;
        if (exe == null) return;
        for (int k = 0; k < 5 && File.Exists(exe + ".old"); k++)
        {
            TryDelete(exe + ".old");
            if (File.Exists(exe + ".old")) Thread.Sleep(300);
        }
        TryDelete(exe + ".new");  // leftover from an interrupted download
    }

    // ---- helpers --------------------------------------------------------

    static bool CanWriteNextToExe()
    {
        try
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath!)!;
            var probe = Path.Combine(dir, ".sounddeck-write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    bool Enter() { lock (_gate) { if (_busy) return false; _busy = true; return true; } }
    void Exit() { lock (_gate) _busy = false; }

    void Set(UpdateStatus s)
    {
        Info.Status = s;
        if (s != UpdateStatus.Error) Info.Error = null;
        Changed?.Invoke();
    }

    void Fail(string stage, Exception ex)
    {
        Info.Error = $"{stage}: {ex.Message}";
        Info.Status = UpdateStatus.Error;
        App.Log("UPDATE " + Info.Error);
        Changed?.Invoke();
    }

    static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }
    static string Format(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";

    public void Dispose() => _http.Dispose();

    // ---- GitHub payload -------------------------------------------------

    sealed record Asset(string Url, long Size, string? Sha256);

    sealed record Release(Version Version, string HtmlUrl, string? Notes, Asset? Asset)
    {
        public static Release? Parse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var tag = r.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var ver)) return null;
            Asset? asset = null;
            foreach (var a in r.GetProperty("assets").EnumerateArray())
            {
                if (!string.Equals(a.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase)) continue;
                string? sha = null;
                if (a.TryGetProperty("digest", out var d) && d.GetString() is string dg && dg.StartsWith("sha256:"))
                    sha = dg["sha256:".Length..];
                asset = new Asset(a.GetProperty("browser_download_url").GetString()!, a.GetProperty("size").GetInt64(), sha);
            }
            string? notes = r.TryGetProperty("body", out var b) ? b.GetString() : null;
            return new Release(Normalize(ver), r.GetProperty("html_url").GetString() ?? "", notes, asset);
        }
    }
}
