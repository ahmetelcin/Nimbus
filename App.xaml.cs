using System.Threading.Channels;
using System.Windows;
using Google.Apis.Auth.OAuth2.Responses;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

namespace DriveAc;

public partial class App : Application
{
    private sealed record WorkItem(Func<Task> Run, TaskCompletionSource<bool> Done);

    // Giriş, çıkış ve yüklemeler sırayla çalışır; iki işlem aynı anda oturuma dokunmaz.
    private readonly Channel<WorkItem> _work = Channel.CreateUnbounded<WorkItem>();
    private readonly CancellationTokenSource _shutdown = new();

    private Notifier _notifier = null!;
    private Session _session = null!;
    private FileOpener _opener = null!;
    private TrayIcon _tray = null!;
    private SignInWindow? _signInWindow;

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
        _tray = new TrayIcon(
            signIn: () => _ = SignInAsync(),
            signOut: () => _ = Enqueue(_session.SignOutAsync),
            exit: ExitApp);

        _session.Changed += OnSessionChanged;
        _notifier.StatusChanged += status => Dispatcher.InvokeAsync(() => _tray.SetStatus(status));

        _ = RunWorkAsync();
        _ = Instance.ListenAsync(
            message => Dispatcher.InvokeAsync(() => OnMessage(message)),
            _shutdown.Token);

        _ = Enqueue(() => LoadSessionAsync(promptIfSignedOut: InitialPaths.Count == 0));
        foreach (var path in InitialPaths)
            _ = Enqueue(() => _opener.OpenAsync(path));
    }

    private void OnMessage(string message)
    {
        if (message.Length > 0)
            _ = Enqueue(() => _opener.OpenAsync(message));
        else if (_session.IsSignedIn)
            _notifier.ShowInfo("DriveAç çalışıyor", "Menüye sistem tepsisindeki simgeden ulaşabilirsiniz.");
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
            catch (Exception ex)
            {
                _notifier.ShowError(ErrorMessages.For(ex));
                item.Done.SetResult(false);
            }
        }
    }

    private void ExitApp()
    {
        _shutdown.Cancel();
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _notifier.ClearProgress();
        _tray.Dispose();
        Shutdown();
    }
}
