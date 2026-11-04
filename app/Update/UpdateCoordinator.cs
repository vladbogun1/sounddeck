using SoundDeck.Model;

namespace SoundDeck.Update;

// Update policy on top of Updater: when to check, when to download, when to ask the
// host to install. The host (App) owns the "is the user looking at the window" part,
// because restarting under the user's cursor is rude.
public sealed class UpdateCoordinator : IDisposable
{
    static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(20);
    static readonly TimeSpan Every = TimeSpan.FromHours(6);

    readonly Updater _updater = new();
    readonly Func<bool> _autoUpdate;
    readonly System.Threading.Timer? _timer;

    public UpdateDto Info => _updater.Info;
    public event Action? Changed;
    // true = install right now (user clicked), false = install when the window is idle.
    public event Action<bool>? InstallRequested;

    public UpdateCoordinator(Func<bool> autoUpdate)
    {
        _autoUpdate = autoUpdate;
        _updater.Changed += () => Changed?.Invoke();
#if DEBUG
        // Dev builds never replace themselves unless explicitly testing the flow.
        if (Environment.GetEnvironmentVariable("SOUNDDECK_FAKE_VERSION") == null) return;
#endif
        _timer = new System.Threading.Timer(_ => _ = ScheduledAsync(), null, FirstCheck, Every);
    }

    async Task ScheduledAsync()
    {
        await _updater.CheckAsync();
        if (_autoUpdate() && Info.Status == UpdateStatus.Available && await _updater.DownloadAsync())
            InstallRequested?.Invoke(false);
        else if (Info.Status == UpdateStatus.Ready && _autoUpdate())
            InstallRequested?.Invoke(false);
    }

    public Task CheckNowAsync() => _updater.CheckAsync();

    public async Task InstallNowAsync()
    {
        if (Info.Status == UpdateStatus.Available || Info.Status == UpdateStatus.Error)
        {
            if (Info.LatestVersion == null) await _updater.CheckAsync();
            if (!await _updater.DownloadAsync()) return;
        }
        if (_updater.HasDownloaded) InstallRequested?.Invoke(true);
    }

    public bool Apply(bool startMinimized) => _updater.ApplyAndRestart(startMinimized);

    public void Dispose()
    {
        _timer?.Dispose();
        _updater.Dispose();
    }
}
