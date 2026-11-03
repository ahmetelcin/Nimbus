using System.Windows;

namespace DriveAc;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // İkinci örneğin çalışma klasörü farklı olabilir; yol tam hâliyle iletilir.
        var paths = args.Where(a => !string.IsNullOrWhiteSpace(a)).Select(Path.GetFullPath).ToList();

        using var instance = new SingleInstance();
        if (!instance.IsPrimary)
        {
            if (SingleInstance.TrySend(paths))
                return 0;

            MessageBox.Show("Çalışan DriveAç uygulamasına ulaşılamadı.", "DriveAç", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }

        var app = new App { InitialPaths = paths, Instance = instance };
        app.InitializeComponent();
        return app.Run();
    }
}
