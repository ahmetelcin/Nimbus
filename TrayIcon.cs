using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using Wpf.Ui.Appearance;
using Icon = System.Drawing.Icon;

namespace Nimbus;

internal sealed record TrayActions(
    Action SignIn,
    Action SignOut,
    Action LinkFileTypes,
    Action CleanOldFiles,
    Action OpenSettings,
    Action Exit);

internal sealed class TrayIcon : IDisposable
{
    private const string DefaultToolTip = "Nimbus";
    private const int MaxToolTipLength = 120;

    private readonly TaskbarIcon _icon;
    private readonly TrayActions _actions;

    private MenuItem _account = null!;
    private MenuItem _signIn = null!;
    private MenuItem _signOut = null!;
    private bool _signedIn;
    private string? _email;

    public TrayIcon(TrayActions actions)
    {
        _actions = actions;

        using var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Nimbus.ico")).Stream;
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
        _signIn = CreateItem("Giriş yap", _actions.SignIn);
        _signOut = CreateItem("Hesaptan çıkış yap", _actions.SignOut);

        var menu = new ContextMenu();
        menu.Items.Add(_account);
        menu.Items.Add(_signIn);
        menu.Items.Add(_signOut);
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateItem("Dosya türlerini bağla…", _actions.LinkFileTypes));
        menu.Items.Add(CreateItem("Eski dosyaları temizle", _actions.CleanOldFiles));
        menu.Items.Add(CreateItem("Ayarlar…", _actions.OpenSettings));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateItem("Nimbus'u kapat", _actions.Exit));

        ApplyAccountState();
        _icon.ContextMenu = menu;
    }

    private static MenuItem CreateItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void ApplyAccountState()
    {
        _account.Header = _signedIn ? _email ?? "Giriş yapıldı" : "Giriş yapılmadı";
        _signIn.Visibility = _signedIn ? Visibility.Collapsed : Visibility.Visible;
        _signOut.Visibility = _signedIn ? Visibility.Visible : Visibility.Collapsed;
    }
}
