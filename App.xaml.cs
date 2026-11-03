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

    // Tarayıcı öne gelip belgeyi göstermeye başlasın; tam ekran denetimi de ona göre yapılsın.
    private static readonly TimeSpan UpdatePromptDelay = TimeSpan.FromSeconds(3);

    private Notifier _notifier = null!;
    private Session _session = null!;
    private FileOpener _opener = null!;
    private SaveBack _saveBack = null!;
    private RecentActions _recent = null!;
    private AutoSync _autoSync = null!;
    private Updater _updater = null!;
    private int _pendingWork;
    private TrayIcon _tray = null!;
    private SignInWindow? _signInWindow;
    private SettingsWindow? _settingsWindow;
    private UpdateWindow? _updateWindow;
    private bool _updatePromptWaiting;

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
        _recent = new RecentActions(_session, _notifier);
        // Bildirim düğmesi başka iş parçacığından gelir; iş kuyruğu UI iş parçacığına aittir.
        Func<Func<Task>, Task<bool>> enqueueFromAnyThread = run => Dispatcher.InvokeAsync(() => Enqueue(run)).Task.Unwrap();
        _autoSync = new AutoSync(_session, _notifier, enqueueFromAnyThread);
        _updater = new Updater(_notifier, enqueueFromAnyThread, () => _pendingWork == 1, RestartAfterUpdate);
        _tray = new TrayIcon(new TrayActions(
            SignIn: () => _ = SignInAsync(),
            SignOut: () => _ = Enqueue(_session.SignOutAsync),
            LinkFileTypes: LinkFileTypes,
            CleanOldFiles: () => _ = Enqueue(CleanOldFilesAsync),
            OpenSettings: ShowSettingsWindow,
            ShowUpdate: ShowUpdateWindow,
            OpenRecent: file => _ = Enqueue(() => _recent.OpenAsync(file)),
            SaveToComputer: file => _ = Enqueue(() => _saveBack.SaveAsync(file)),
            CopyPrivateLink: file => _ = Enqueue(() => _recent.CopyPrivateLinkAsync(file)),
            CopyPublicLink: file => _ = Enqueue(() => _recent.CopyPublicLinkAsync(file)),
            StopSharing: file => _ = Enqueue(() => _recent.StopSharingAsync(file)),
            ShowInFolder: file => _ = Enqueue(() =>
            {
                RecentActions.ShowInFolder(file);
                return Task.CompletedTask;
            }),
            Exit: ExitApp));
        RefreshRecentFiles();

        FileAssociations.RefreshIfRegistered();
        AutoStart.RefreshIfEnabled();
        ExplorerMenu.Refresh();

        _session.Changed += OnSessionChanged;
        _notifier.StatusChanged += status => Dispatcher.InvokeAsync(() => _tray.SetStatus(status));
        _updater.StateChanged += () => Dispatcher.InvokeAsync(OnUpdateStateChanged);
        _updater.PromptRequested += () => Dispatcher.InvokeAsync(ShowUpdateWindow);
        FileOpener.DocumentOpened += () => _ = PromptForUpdateAsync();

        _ = RunWorkAsync();
        _ = Instance.ListenAsync(
            message => Dispatcher.InvokeAsync(() => OnMessage(message)),
            _shutdown.Token);

        _ = Enqueue(() => LoadSessionAsync(promptIfSignedOut: InitialPaths.Count == 0));
        foreach (var path in InitialPaths)
            _ = Enqueue(() => _opener.OpenAsync(path));
        _ = _autoSync.RunAsync(_shutdown.Token);

        if (File.Exists(UpdateInstaller.OldPath(Environment.ProcessPath!)))
            _notifier.ShowInfo(Strings.UpdateDoneTitle, string.Format(Strings.UpdateDoneBody, UpdateInstaller.CurrentVersion));
        _ = UpdateInstaller.DeleteOldAsync(Environment.ProcessPath!);
        _ = _updater.RunAsync(_shutdown.Token);
    }

    private void OnMessage(string message)
    {
        if (message.Length > 0)
            _ = Enqueue(() => _opener.OpenAsync(message));
        else if (_session.IsSignedIn)
            _notifier.ShowInfo(Strings.RunningTitle, Strings.RunningBody);
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
            _notifier.ShowInfo(Strings.SessionExpiredTitle, Strings.SessionExpiredBody);
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
            _settingsWindow = new SettingsWindow(LinkFileTypes, UnlinkFileTypes, OnLanguageChanged, _updater.OnModeChanged);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }

        _settingsWindow.Activate();
    }

    private void OnUpdateStateChanged()
    {
        var row = _updater.Row;
        _tray.SetUpdate(row);
        // Kurulum başladıysa ya da mod değiştiyse artık sorulacak bir şey yoktur.
        if (row is not { Installing: false })
            _updateWindow?.Withdraw();
    }

    // Tepsiden ya da bildirimden istendiğinde tam ekran ve "Sonra" sınırı uygulanmaz.
    private void ShowUpdateWindow()
    {
        if (CreateUpdateWindow() is { } window)
            window.Show();

        _updateWindow?.BringForward();
    }

    // Belge açılışını geciktirmesin ve kuyruğu tutmasın diye iş kuyruğunun dışındadır.
    private async Task PromptForUpdateAsync()
    {
        if (_updatePromptWaiting || !_updater.IsPromptDue)
            return;

        _updatePromptWaiting = true;
        try
        {
            await Task.Delay(UpdatePromptDelay, _shutdown.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            _updatePromptWaiting = false;
        }

        // Tam ekrandaysa ertelenmiş sayılmaz; bir sonraki dosyada yeniden denenir.
        if (_updater.IsPromptDue && !FullScreen.IsActive())
            CreateUpdateWindow()?.ShowQuietly();
    }

    // Pencere zaten açıksa ya da gösterilecek sürüm yoksa null döner.
    private UpdateWindow? CreateUpdateWindow()
    {
        if (_updateWindow is not null || _updater.Row is not { Installing: false } row)
            return null;

        var window = new UpdateWindow(row.Version, UpdateInstaller.CurrentVersion.ToString());
        window.Closed += (_, _) =>
        {
            _updateWindow = null;
            if (window.Choice is { } choice)
                _updater.Accept(choice);
            else if (window.IsPostponed)
                _updater.Snooze();
        };
        _updateWindow = window;
        return window;
    }

    // Menü, Explorer kayıtları ve Ayarlar penceresi metinlerini kurulurken aldığı için yeniden kurulur.
    private void OnLanguageChanged()
    {
        Localization.Apply();
        _tray.RebuildMenu();
        ExplorerMenu.Refresh();
        FileAssociations.RefreshIfRegistered();
        _updateWindow?.Withdraw();
        _settingsWindow?.Close();
        Dispatcher.BeginInvoke(ShowSettingsWindow);
    }

    // Windows, varsayılan uygulamanın programla seçilmesine izin vermez; kullanıcı Ayarlar'dan seçer.
    private void LinkFileTypes()
    {
        FileAssociations.Register();
        Process.Start(new ProcessStartInfo(FileAssociations.SettingsUri) { UseShellExecute = true });
        _notifier.ShowInfo(Strings.FileTypesSavedTitle, Strings.FileTypesSavedBody);
    }

    private void UnlinkFileTypes()
    {
        FileAssociations.Unregister();
        _notifier.ShowInfo(Strings.FileTypesRemovedTitle, Strings.FileTypesRemovedBody);
    }

    private async Task CleanOldFilesAsync()
    {
        var credential = await _session.GetCredentialAsync();
        using var uploader = new DriveUploader(credential);

        var days = AppSettings.Load().CleanupDays;
        var fileIds = await uploader.ListFilesOlderThanAsync(days);
        if (fileIds.Count == 0)
        {
            _notifier.ShowInfo(Strings.CleanNothingTitle, string.Format(Strings.CleanNothingBody, days));
            return;
        }

        var confirm = new Wpf.Ui.Controls.MessageBox
        {
            Title = Strings.CleanPromptTitle,
            Content = string.Format(Strings.CleanPromptBody, days, fileIds.Count),
            PrimaryButtonText = Strings.CleanPrimary,
            CloseButtonText = Strings.Cancel,
        };
        if (await confirm.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
            return;

        await uploader.MoveToTrashAsync(fileIds);
        _notifier.ShowInfo(Strings.CleanDoneTitle, string.Format(Strings.CleanDoneBody, fileIds.Count));
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
        _pendingWork++;
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
                _notifier.ShowError(Strings.SessionExpiredError);
                item.Done.SetResult(false);
            }
            catch (Exception ex)
            {
                _notifier.ShowError(ErrorMessages.For(ex));
                item.Done.SetResult(false);
            }

            _pendingWork--;
            RefreshRecentFiles();
        }
    }

    private void RefreshRecentFiles() => _tray.SetRecentFiles(UploadHistory.Recent(RecentFileCount));

    private void ExitApp()
    {
        ReleaseResources();
        Shutdown();
    }

    private void RestartAfterUpdate(string exe)
    {
        ReleaseResources();
        // Yeni süreç, tek örnek kilidi bırakılmadan başlarsa kendini ikinci örnek sayıp kapanır.
        Instance.Dispose();
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            UpdateInstaller.Rollback(exe);
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
        }

        Shutdown();
    }

    private void ReleaseResources()
    {
        _shutdown.Cancel();
        // Uygulama kapanırken pencerenin kapanması "Sonra" sayılmamalı.
        _updateWindow?.Withdraw();
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _notifier.ClearProgress();
        _tray.Dispose();
    }
}
