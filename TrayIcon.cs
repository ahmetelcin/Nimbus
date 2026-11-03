using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using Wpf.Ui.Appearance;
using Icon = System.Drawing.Icon;

namespace DriveAc;

internal sealed class TrayIcon : IDisposable
{
    private const string DefaultToolTip = "DriveAç";
    private const int MaxToolTipLength = 120;

    private readonly TaskbarIcon _icon;
    private readonly Action _signInAction;
    private readonly Action _signOutAction;
    private readonly Action _exitAction;

    private MenuItem _account = null!;
    private MenuItem _signIn = null!;
    private MenuItem _signOut = null!;
    private bool _signedIn;
    private string? _email;

    public TrayIcon(Action signIn, Action signOut, Action exit)
    {
        _signInAction = signIn;
        _signOutAction = signOut;
        _exitAction = exit;

        using var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/DriveAc.ico")).Stream;
        _icon = new TaskbarIcon
        {
            Icon = new Icon(iconStream),
            ToolTipText = DefaultToolTip,
        };
        RebuildMenu();
        _icon.ForceCreate(enablesEfficiencyMode: false);

        ApplicationThemeManager.Changed += OnThemeChanged;
    }

    public void SetAccount(bool signedIn, string? email)
    {
        _signedIn = signedIn;
        _email = email;
        ApplyAccountState();
    }

    public void SetStatus(string? status)
    {
        var text = status ?? DefaultToolTip;
        _icon.ToolTipText = text.Length <= MaxToolTipLength ? text : text[..MaxToolTipLength] + "…";
    }

    public void Dispose()
    {
        ApplicationThemeManager.Changed -= OnThemeChanged;
        _icon.Dispose();
    }

    // Menü hiçbir pencerenin ağacında olmadığı için WPF tema kaynaklarını ona yansıtmaz; yeniden kurulması gerekir.
    private void OnThemeChanged(ApplicationTheme theme, System.Windows.Media.Color accent) => RebuildMenu();

    private void RebuildMenu()
    {
        _account = new MenuItem { IsEnabled = false };
        _signIn = new MenuItem { Header = "Giriş yap" };
        _signOut = new MenuItem { Header = "Çıkış yap" };
        var exit = new MenuItem { Header = "Çıkış" };

        _signIn.Click += (_, _) => _signInAction();
        _signOut.Click += (_, _) => _signOutAction();
        exit.Click += (_, _) => _exitAction();

        var menu = new ContextMenu();
        menu.Items.Add(_account);
        menu.Items.Add(new Separator());
        menu.Items.Add(_signIn);
        menu.Items.Add(_signOut);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);

        ApplyAccountState();
        _icon.ContextMenu = menu;
    }

    private void ApplyAccountState()
    {
        _account.Header = _signedIn ? _email ?? "Giriş yapıldı" : "Giriş yapılmadı";
        _signIn.Visibility = _signedIn ? Visibility.Collapsed : Visibility.Visible;
        _signOut.Visibility = _signedIn ? Visibility.Visible : Visibility.Collapsed;
    }
}
