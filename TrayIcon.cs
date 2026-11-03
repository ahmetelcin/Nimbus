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
    Action<RecentFile> OpenRecent,
    Action<RecentFile> SaveToComputer,
    Action<RecentFile> CopyPrivateLink,
    Action<RecentFile> CopyPublicLink,
    Action<RecentFile> StopSharing,
    Action<RecentFile> ShowInFolder,
    Action Exit);

internal sealed class TrayIcon : IDisposable
{
    private const string DefaultToolTip = "Nimbus";
    private const int MaxToolTipLength = 120;
    private const double MaxFileNameWidth = 320;

    private readonly TaskbarIcon _icon;
    private readonly TrayActions _actions;

    private MenuItem _account = null!;
    private MenuItem _signIn = null!;
    private MenuItem _signOut = null!;
    private MenuItem _recentMenu = null!;
    private Separator _recentSeparator = null!;
    private bool _signedIn;
    private string? _email;
    private IReadOnlyList<RecentFile> _recentFiles = [];

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

    public void SetRecentFiles(IReadOnlyList<RecentFile> files)
    {
        // Her arka plan turunda yeniden kurulsa açık alt menü kapanırdı.
        if (files.SequenceEqual(_recentFiles))
            return;

        _recentFiles = files;
        ApplyRecentFiles();
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
        _recentMenu = new MenuItem { Header = "Son dosyalar" };
        _recentSeparator = new Separator();

        var menu = new ContextMenu();
        menu.Items.Add(_account);
        menu.Items.Add(_signIn);
        menu.Items.Add(_signOut);
        menu.Items.Add(_recentSeparator);
        menu.Items.Add(_recentMenu);
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateItem("Dosya türlerini bağla…", _actions.LinkFileTypes));
        menu.Items.Add(CreateItem("Eski dosyaları temizle", _actions.CleanOldFiles));
        menu.Items.Add(CreateItem("Ayarlar…", _actions.OpenSettings));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateItem("Nimbus'u kapat", _actions.Exit));

        ApplyAccountState();
        ApplyRecentFiles();
        // "2 saat önce" gibi metinler menü her açıldığında tazelenir.
        menu.Opened += (_, _) => ApplyRecentFiles();
        _icon.ContextMenu = menu;
    }

    private void ApplyRecentFiles()
    {
        _recentMenu.Items.Clear();
        foreach (var file in _recentFiles)
            _recentMenu.Items.Add(CreateRecentItem(file));
        _recentMenu.IsEnabled = _recentFiles.Count > 0;
    }

    private MenuItem CreateRecentItem(RecentFile file)
    {
        var item = new MenuItem { Header = CreateRecentHeader(file) };

        var copyLink = new MenuItem { Header = "Bağlantıyı kopyala" };
        copyLink.Items.Add(CreateItem("Sadece benim için", () => _actions.CopyPrivateLink(file)));
        copyLink.Items.Add(CreateItem("Bağlantıya sahip herkes görüntüleyebilir…", () => _actions.CopyPublicLink(file)));

        item.Items.Add(CreateItem("Aç", () => _actions.OpenRecent(file)));
        item.Items.Add(CreateItem("Bilgisayara kaydet", () => _actions.SaveToComputer(file)));
        item.Items.Add(copyLink);
        item.Items.Add(CreateItem("Paylaşımı kapat", () => _actions.StopSharing(file)));
        item.Items.Add(CreateItem("Klasörde göster", () => _actions.ShowInFolder(file)));

        // Alt menüsü olan öğe Click olayı üretmez; satırın kendisine tıklamak belgeyi açar.
        item.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!ReferenceEquals(OwningMenuItem(e.OriginalSource as DependencyObject), item))
                return;

            e.Handled = true;
            _icon.ContextMenu!.IsOpen = false;
            _actions.OpenRecent(file);
        };
        return item;
    }

    private static StackPanel CreateRecentHeader(RecentFile file)
    {
        // Düz metin başlıkta WPF alt çizgiyi kısayol sayıp dosya adından siler.
        var name = new TextBlock
        {
            Text = Path.GetFileName(file.Path),
            MaxWidth = MaxFileNameWidth,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var opened = new TextBlock
        {
            Text = file.OpenedAgo(DateTime.UtcNow),
            FontSize = 12,
            Opacity = 0.7,
        };

        var header = new StackPanel();
        header.Children.Add(name);
        header.Children.Add(opened);
        return header;
    }

    private static MenuItem? OwningMenuItem(DependencyObject? element)
    {
        while (element is not null and not MenuItem)
        {
            element = element is System.Windows.Media.Visual
                ? System.Windows.Media.VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return element as MenuItem;
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
        _recentMenu.Visibility = _signOut.Visibility;
        _recentSeparator.Visibility = _signOut.Visibility;
    }
}
