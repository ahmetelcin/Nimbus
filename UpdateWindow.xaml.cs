using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace Nimbus;

public partial class UpdateWindow : FluentWindow
{
    private const double ScreenMargin = 16;
    private static readonly TimeSpan FullScreenCheckInterval = TimeSpan.FromSeconds(2);

    private DispatcherTimer? _fullScreenWatch;
    private bool _withdrawn;

    internal UpdateWindow(string newVersion, string currentVersion)
    {
        InitializeComponent();
        VersionsText.Text = string.Format(Strings.UpdateWindowVersions, newVersion, currentVersion);
        // Yükseklik içeriğe göre belirlenir; pencere bu yüzden boyutu kesinleşince yerleşir.
        SizeChanged += (_, _) => MoveToCorner();
    }

    internal UpdateChoice? Choice { get; private set; }

    // Pencereyi X ile kapatmak da "Sonra" demektir; yoksa bir sonraki dosyada yine sorulurdu.
    internal bool IsPostponed => Choice is null && !_withdrawn;

    // Kullanıcı belgede yazarken açılır: odağı almaz, tuşlar yanlışlıkla düğmeye basmaz.
    internal void ShowQuietly()
    {
        ShowActivated = false;
        _fullScreenWatch = new DispatcherTimer { Interval = FullScreenCheckInterval };
        _fullScreenWatch.Tick += (_, _) =>
        {
            if (FullScreen.IsActive())
                Withdraw();
        };
        _fullScreenWatch.Start();
        Show();
    }

    // Tepsiden ya da bildirimden açılınca kullanıcı pencereyi kendisi istemiştir.
    internal void BringForward()
    {
        _fullScreenWatch?.Stop();
        Activate();
    }

    // Kullanıcı yanıt vermeden kapanır; "Sonra" sayılmaz.
    internal void Withdraw()
    {
        _withdrawn = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _fullScreenWatch?.Stop();
        base.OnClosed(e);
    }

    private void MoveToCorner()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - ScreenMargin;
        Top = area.Bottom - ActualHeight - ScreenMargin;
    }

    private void OnNowClick(object sender, RoutedEventArgs e) => Finish(UpdateChoice.Now);

    private void OnAlwaysClick(object sender, RoutedEventArgs e) => Finish(UpdateChoice.Always);

    private void OnLaterClick(object sender, RoutedEventArgs e) => Close();

    private void Finish(UpdateChoice choice)
    {
        Choice = choice;
        Close();
    }
}
