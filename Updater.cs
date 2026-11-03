using System.Net;

namespace Nimbus;

internal enum UpdateChoice
{
    Now,
    Always,
}

internal sealed record UpdateRow(string Version, bool Installing);

// Yeni sürümü denetler, doğrular ve iş kuyruğu boşken kurar.
internal sealed class Updater(
    Notifier notifier,
    Func<Func<Task>, Task<bool>> enqueue,
    Func<bool> isIdle,
    Action<string> restart)
{
    private const string RepoUrl = "https://github.com/ahmetelcin/Nimbus/releases/download/";
    private const string ApiUrl = "https://api.github.com/repos/ahmetelcin/Nimbus/releases/latest";
    private const string ExeName = "Nimbus.exe";
    private const string HashName = "Nimbus.exe.sha256";
    private const string SignatureName = "Nimbus.exe.sig";

    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);

    private static readonly HttpClient Http = CreateClient();

    private readonly string _exe = Environment.ProcessPath!;
    private int _installing;
    private bool _readyToApply;
    private bool _userInstall;
    private string _userInstallVersion = "";

    // Başka bir iş parçacığından gelebilir.
    public event Action? StateChanged;

    // Bildirime tıklanınca gelir; başka bir iş parçacığından olabilir.
    public event Action? PromptRequested;

    // Tepsi menüsünün en üstündeki satır; gösterilecek bir şey yoksa null.
    public UpdateRow? Row
    {
        get
        {
            if (IsUserInstalling)
                return new UpdateRow(_userInstallVersion, Installing: true);

            var settings = AppSettings.Load();
            return settings.UpdateMode == UpdateMode.Ask && StoredUpdate(settings) is { } update
                ? new UpdateRow(update.Version.ToString(), Installing: false)
                : null;
        }
    }

    public bool IsPromptDue
    {
        get
        {
            if (Row is not { Installing: false } row)
                return false;

            var settings = AppSettings.Load();
            return UpdatePrompt.IsDue(row.Version, settings.UpdateSnoozedVersion, settings.UpdateSnoozedUtcTicks, DateTime.UtcNow.Ticks);
        }
    }

    private bool IsUserInstalling => _userInstall && (_readyToApply || Volatile.Read(ref _installing) == 1);

    public void Accept(UpdateChoice choice)
    {
        var settings = AppSettings.Load();
        if (_readyToApply || StoredUpdate(settings) is not { } update)
            return;

        if (choice == UpdateChoice.Always)
        {
            settings.UpdateMode = UpdateMode.Automatic;
            settings.Save();
        }

        notifier.DismissUpdateAvailable();
        _userInstallVersion = update.Version.ToString();
        _userInstall = true;
        var install = InstallAsync(update.Tag, update.Version, reportNetworkErrors: true);
        StateChanged?.Invoke();
        _ = install;
    }

    public void Snooze()
    {
        var settings = AppSettings.Load();
        if (StoredUpdate(settings) is not { } update)
            return;

        settings.UpdateSnoozedVersion = update.Version.ToString();
        settings.UpdateSnoozedUtcTicks = DateTime.UtcNow.Ticks;
        settings.Save();
        notifier.DismissUpdateAvailable();
    }

    // Ayarlardan otomatiğe geçilince bekleyen sürüm 24 saatlik denetimi beklemeden kurulur.
    public void OnModeChanged()
    {
        var settings = AppSettings.Load();
        if (settings.UpdateMode == UpdateMode.Automatic && StoredUpdate(settings) is { } update)
            _ = InstallAsync(update.Tag, update.Version, reportNetworkErrors: true);

        StateChanged?.Invoke();
    }

    public async Task RunAsync(CancellationToken cancellation)
    {
        if (!UpdateInstaller.CanSelfUpdate)
            return;

        // Önceki oturumda bulunan sürüm için satır hemen görünür.
        StateChanged?.Invoke();

        try
        {
            await Task.Delay(StartupDelay, cancellation);
            await TickAsync();

            using var timer = new PeriodicTimer(TickInterval);
            while (await timer.WaitForNextTickAsync(cancellation))
                await TickAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task TickAsync()
    {
        // Kuyruk doluyken ertelenen kurulum yeniden denenir.
        if (_readyToApply)
        {
            await enqueue(ApplyIfIdleAsync);
            return;
        }

        var settings = AppSettings.Load();
        if (settings.UpdateMode == UpdateMode.Off || !IsCheckDue(settings))
            return;

        await CheckAsync();
    }

    private static bool IsCheckDue(AppSettings settings)
    {
#if DEBUG
        if (TestBaseUrl is not null)
            return true;
#endif
        return DateTime.UtcNow.Ticks - settings.LastUpdateCheckUtcTicks >= CheckInterval.Ticks;
    }

    private async Task CheckAsync()
    {
        string json;
        try
        {
            json = await Http.GetStringAsync(LatestUrl);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // İnternet yok ya da GitHub yanıt vermiyor: sessizce sonraki tura kalır.
            return;
        }

        var settings = AppSettings.Load();
        settings.LastUpdateCheckUtcTicks = DateTime.UtcNow.Ticks;
        settings.Save();

        if (UpdateInstaller.ParseLatest(json) is not { } latest)
            return;

        // Sürüm geri çekildiyse bekleyen kayıt ve tepsi satırı da kalkar.
        var isNewer = UpdateInstaller.IsNewer(latest.Version, UpdateInstaller.CurrentVersion);
        settings.AvailableUpdateTag = isNewer ? latest.Tag : null;
        settings.Save();
        StateChanged?.Invoke();
        if (!isNewer)
            return;

        if (settings.UpdateMode == UpdateMode.Automatic)
        {
            await InstallAsync(latest.Tag, latest.Version, reportNetworkErrors: false);
            return;
        }

        var version = latest.Version.ToString();
        if (UpdatePrompt.IsDue(version, settings.UpdateSnoozedVersion, settings.UpdateSnoozedUtcTicks, DateTime.UtcNow.Ticks))
            notifier.ShowUpdateAvailable(version, () => PromptRequested?.Invoke());
    }

    // Geliştirme derlemesi kendini güncelleyemez ama ayar dosyasını paylaşır.
    private static (string Tag, Version Version)? StoredUpdate(AppSettings settings) =>
        UpdateInstaller.CanSelfUpdate
        && UpdateInstaller.ParseTag(settings.AvailableUpdateTag) is { } update
        && UpdateInstaller.IsNewer(update.Version, UpdateInstaller.CurrentVersion)
            ? update
            : null;

    private async Task InstallAsync(string tag, Version version, bool reportNetworkErrors)
    {
        if (Interlocked.Exchange(ref _installing, 1) == 1)
            return;

        var newExe = UpdateInstaller.NewPath(_exe);
        try
        {
            using var timeout = new CancellationTokenSource(DownloadTimeout);
            var hash = await DownloadTextAsync(tag, HashName, timeout.Token);
            var signature = await DownloadTextAsync(tag, SignatureName, timeout.Token);
            await DownloadFileAsync(tag, ExeName, newExe, timeout.Token);

            UpdateInstaller.VerifyPackage(newExe, hash, signature, UpdateKey.PublicKey, version, UpdateInstaller.CurrentVersion);

            _readyToApply = true;
            await enqueue(ApplyIfIdleAsync);
        }
        catch (UserMessageException ex)
        {
            LocalFiles.TryDelete(newExe);
            notifier.ShowError(ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            LocalFiles.TryDelete(newExe);
            if (reportNetworkErrors)
                notifier.ShowError(Strings.UpdateDownloadFailed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LocalFiles.TryDelete(newExe);
            notifier.ShowError(Strings.UpdateWriteFailed);
        }
        finally
        {
            Interlocked.Exchange(ref _installing, 0);
            // Kurulum başarısızsa satır yeniden tıklanabilir olur.
            _userInstall &= _readyToApply;
            StateChanged?.Invoke();
        }
    }

    // Kuyrukta yalnızca bu iş varsa yükleme, eşitleme ya da açık bir soru yoktur.
    private Task ApplyIfIdleAsync()
    {
        if (!_readyToApply || !isIdle())
            return Task.CompletedTask;

        _readyToApply = false;
        try
        {
            UpdateInstaller.Swap(_exe, UpdateInstaller.NewPath(_exe));
        }
        catch
        {
            _userInstall = false;
            StateChanged?.Invoke();
            throw;
        }

        restart(_exe);
        return Task.CompletedTask;
    }

    private static async Task<string> DownloadTextAsync(string tag, string name, CancellationToken cancellation)
    {
        using var response = await Http.GetAsync(AssetUrl(tag, name), cancellation);
        EnsureAssetExists(response, name);
        return await response.Content.ReadAsStringAsync(cancellation);
    }

    private static async Task DownloadFileAsync(string tag, string name, string destination, CancellationToken cancellation)
    {
        using var response = await Http.GetAsync(AssetUrl(tag, name), HttpCompletionOption.ResponseHeadersRead, cancellation);
        EnsureAssetExists(response, name);
        await using var file = File.Create(destination);
        await response.Content.CopyToAsync(file, cancellation);
    }

    private static void EnsureAssetExists(HttpResponseMessage response, string name)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new UserMessageException(string.Format(Strings.UpdateAssetMissing, name));
        response.EnsureSuccessStatusCode();
    }

#if DEBUG
    // Yalnızca geliştirme derlemesinde: yerel test sunucusu. Release derlemesinde bu yol yoktur.
    private static readonly string? TestBaseUrl = Environment.GetEnvironmentVariable("NIMBUS_UPDATE_BASE");

    private static string LatestUrl => TestBaseUrl is { } test ? test + "latest.json" : ApiUrl;

    private static string AssetUrl(string tag, string name) => (TestBaseUrl ?? RepoUrl) + $"{tag}/{name}";
#else
    private static string LatestUrl => ApiUrl;

    private static string AssetUrl(string tag, string name) => $"{RepoUrl}{tag}/{name}";
#endif

    private static HttpClient CreateClient()
    {
        // Süre sınırı indirme başına ayrıca verilir; varsayılan 100 saniye büyük dosyaya yetmez.
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        // GitHub API'si User-Agent olmayan istekleri reddeder.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Nimbus/{UpdateInstaller.CurrentVersion}");
        return client;
    }
}
