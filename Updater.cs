using System.Net;

namespace Nimbus;

// Yeni sürümü denetler, doğrular ve iş kuyruğu boşken kurar.
internal sealed class Updater(
    Notifier notifier,
    Func<Func<Task>, Task<bool>> enqueue,
    Func<bool> isIdle,
    Action<string> restart)
{
    private const string RepoUrl = "https://github.com/ahmetelcin/Nimbus/releases/download/";
    private const string ApiUrl = "https://api.github.com/repos/ahmetelcin/Nimbus/releases/latest";
    private const string ZipName = "Nimbus-win-x64.zip";
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

    public async Task RunAsync(CancellationToken cancellation)
    {
        if (!UpdateInstaller.CanSelfUpdate)
            return;

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

        if (UpdateInstaller.ParseLatest(json) is not { } latest || !UpdateInstaller.IsNewer(latest.Version, UpdateInstaller.CurrentVersion))
            return;

        if (settings.UpdateMode == UpdateMode.Automatic)
        {
            await InstallAsync(latest.Tag, latest.Version, reportNetworkErrors: false);
            return;
        }

        notifier.ShowUpdateAvailable(latest.Version.ToString(), choice =>
        {
            if (choice == UpdateChoice.Always)
            {
                var current = AppSettings.Load();
                current.UpdateMode = UpdateMode.Automatic;
                current.Save();
            }

            _ = InstallAsync(latest.Tag, latest.Version, reportNetworkErrors: true);
        });
    }

    private async Task InstallAsync(string tag, Version version, bool reportNetworkErrors)
    {
        if (Interlocked.Exchange(ref _installing, 1) == 1)
            return;

        var newExe = UpdateInstaller.NewPath(_exe);
        var zip = Path.Combine(Path.GetTempPath(), $"nimbus-update-{Guid.NewGuid():N}.zip");
        try
        {
            using var timeout = new CancellationTokenSource(DownloadTimeout);
            var hash = await DownloadTextAsync(tag, HashName, timeout.Token);
            var signature = await DownloadTextAsync(tag, SignatureName, timeout.Token);
            await DownloadFileAsync(tag, ZipName, zip, timeout.Token);

            UpdateInstaller.ExtractExe(zip, newExe);
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
                notifier.ShowError("Güncelleme indirilemedi. İnternet bağlantınızı kontrol edin.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LocalFiles.TryDelete(newExe);
            notifier.ShowError("Güncelleme dosyaları yazılamadığı için güncelleme kurulmadı.");
        }
        finally
        {
            LocalFiles.TryDelete(zip);
            Interlocked.Exchange(ref _installing, 0);
        }
    }

    // Kuyrukta yalnızca bu iş varsa yükleme, eşitleme ya da açık bir soru yoktur.
    private Task ApplyIfIdleAsync()
    {
        if (!_readyToApply || !isIdle())
            return Task.CompletedTask;

        _readyToApply = false;
        UpdateInstaller.Swap(_exe, UpdateInstaller.NewPath(_exe));
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
            throw new UserMessageException($"Yeni sürümde {name} bulunmuyor; güncelleme kurulmadı.");
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
