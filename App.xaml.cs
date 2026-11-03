using System.Diagnostics;
using System.Threading.Channels;
using System.Windows;
using Google.Apis.Auth.OAuth2.Responses;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

namespace Nimbus;

public partial class App : Application
{
    private sealed record WorkItem(Func<Task> Run, TaskCompletionSource<bool> Done);

    // Giriş, çıkış ve yüklemeler sırayla çalışır; iki işlem aynı anda oturuma dokunmaz.
    private readonly Channel<WorkItem> _work = Channel.CreateUnbounded<WorkItem>();
    private readonly CancellationTokenSource _shutdown = new();

    private const int RecentFileCount = 10;

    private Notifier _notifier = null!;
    private Session _session = null!;
    private FileOpener _opener = null!;
    private SaveBack _saveBack = null!;
    private AutoSync _autoSync = null!;
    private TrayIcon _tray = null!;
    private SignInWindow? _signInWindow;
    private SettingsWindow? _settingsWindow;

    internal IReadOnlyList<string> InitialPaths { get; init; } = [];

    internal SingleInstance Instance { get; init; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ApplicationThemeManager.ApplySystemTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        _notifier = new Notifier();
        _session = new Session(_notifier);
        _opener = new FileOpener(_session, _notifier);
        _saveBack = new SaveBack(_session, _notifier);
        // Bildirim düğmesi başka iş parçacığından gelir; iş kuyruğu UI iş parçacığına aittir.
        _autoSync = new AutoSync(_session, _notifier, run => Dispatcher.InvokeAsync(() => Enqueue(run)).Task.Unwrap());
        _tray = new TrayIcon(new TrayActions(
            SignIn: () => _ = SignInAsync(),
            SignOut: () => _ = Enqueue(_session.SignOutAsync),
            LinkFileTypes: LinkFileTypes,
            CleanOldFiles: () => _ = Enqueue(CleanOldFilesAsync),
            OpenSettings: ShowSettingsWindow,
            SaveToComputer: file => _ = Enqueue(() => _saveBack.SaveAsync(file)),
            Exit: ExitApp));
        RefreshRecentFiles();

        FileAssociations.RefreshIfRegistered();
        AutoStart.RefreshIfEnabled();

        _session.Changed += OnSessionChanged;
        _notifier.StatusChanged += status => Dispatcher.InvokeAsync(() => _tray.SetStatus(status));

        _ = RunWorkAsync();
        _ = Instance.ListenAsync(
            message => Dispatcher.InvokeAsync(() => OnMessage(message)),
            _shutdown.Token);

        _ = Enqueue(() => LoadSessionAsync(promptIfSignedOut: InitialPaths.Count == 0));
        foreach (var path in InitialPaths)
            _ = Enqueue(() => _opener.OpenAsync(path));
        _ = _autoSync.RunAsync(_shutdown.Token);
    }

    private void OnMessage(string message)
    {
        if (message.Length > 0)
            _ = Enqueue(() => _opener.OpenAsync(message));
        else if (_session.IsSignedIn)
            _notifier.ShowInfo("Nimbus çalışıyor", "Menüye sistem tepsisindeki simgeden ulaşabilirsiniz.");
        else
            ShowSignInWindow();
    }

    private async Task LoadSessionAsync(bool promptIfSignedOut)
    {
        try
        {
            await _session.LoadAsync();
        }
        catch (TokenResponseException)
        {
            await _session.ExpireAsync();
            _notifier.ShowInfo("Oturumunuz sona erdi", "Devam etmek için yeniden giriş yapın.");
        }

        if (promptIfSignedOut && !_session.IsSignedIn)
            ShowSignInWindow();
    }

    private Task<bool> SignInAsync() => Enqueue(_session.GetCredentialAsync);

    private void ShowSignInWindow()
    {
        if (_signInWindow is null)
        {
            _signInWindow = new SignInWindow(SignInAsync);
            _signInWindow.Closed += (_, _) => _signInWindow = null;
            _signInWindow.Show();
        }

        _signInWindow.Activate();
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(LinkFileTypes, UnlinkFileTypes);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }

        _settingsWindow.Activate();
    }

    // Windows, varsayılan uygulamanın programla seçilmesine izin vermez; kullanıcı Ayarlar'dan seçer.
    private void LinkFileTypes()
    {
        FileAssociations.Register();
        Process.Start(new ProcessStartInfo(FileAssociations.SettingsUri) { UseShellExecute = true });
        _notifier.ShowInfo("Dosya türleri kaydedildi", "Açılan sayfada her uzantı için Nimbus'u varsayılan yapın.");
    }

    private void UnlinkFileTypes()
    {
        FileAssociations.Unregister();
        _notifier.ShowInfo("Dosya türü bağlantıları kaldırıldı", "Nimbus artık Varsayılan uygulamalar listesinde görünmez.");
    }

    private async Task CleanOldFilesAsync()
    {
        var credential = await _session.GetCredentialAsync();
        using var uploader = new DriveUploader(credential);

        var days = AppSettings.Load().CleanupDays;
        var fileIds = await uploader.ListFilesOlderThanAsync(days);
        if (fileIds.Count == 0)
        {
            _notifier.ShowInfo("Temizlenecek dosya yok", $"Nimbus klasöründe {days} günden eski dosya bulunmuyor.");
            return;
        }

        var confirm = new Wpf.Ui.Controls.MessageBox
        {
            Title = "Eski dosyaları temizle",
            Content = $"Nimbus klasöründe {days} gündür değişmeyen {fileIds.Count} dosya çöp kutusuna taşınacak.",
            PrimaryButtonText = "Çöp kutusuna taşı",
            CloseButtonText = "Vazgeç",
        };
        if (await confirm.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
            return;

        await uploader.MoveToTrashAsync(fileIds);
        _notifier.ShowInfo("Temizlik tamamlandı", $"{fileIds.Count} dosya çöp kutusuna taşındı.");
    }

    private void OnSessionChanged()
    {
        _tray.SetAccount(_session.IsSignedIn, _session.Email);
        if (_session.IsSignedIn)
            _signInWindow?.Close();
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
            Dispatcher.InvokeAsync(() => ApplicationThemeManager.ApplySystemTheme());
    }

    private Task<bool> Enqueue(Func<Task> run)
    {
        var done = new TaskCompletionSource<bool>();
        _work.Writer.TryWrite(new WorkItem(run, done));
        return done.Task;
    }

    private async Task RunWorkAsync()
    {
        await foreach (var item in _work.Reader.ReadAllAsync())
        {
            try
            {
                await item.Run();
                item.Done.SetResult(true);
            }
            catch (TokenResponseException)
            {
                await _session.ExpireAsync();
                _notifier.ShowError("Oturumunuz sona erdi. Yeniden giriş yapıp tekrar deneyin.");
                item.Done.SetResult(false);
            }
            catch (Exception ex)
            {
                _notifier.ShowError(ErrorMessages.For(ex));
                item.Done.SetResult(false);
            }

            RefreshRecentFiles();
        }
    }

    private void RefreshRecentFiles() => _tray.SetRecentFiles(UploadHistory.Recent(RecentFileCount));

    private void ExitApp()
    {
        _shutdown.Cancel();
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _notifier.ClearProgress();
        _tray.Dispose();
        Shutdown();
    }
}
