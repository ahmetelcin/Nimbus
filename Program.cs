using System.Windows;

namespace Nimbus;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Localization.Apply();

        // İkinci örneğin çalışma klasörü farklı olabilir; yol tam hâliyle iletilir.
        var paths = args.Where(a => !string.IsNullOrWhiteSpace(a)).Select(Path.GetFullPath).ToList();

        using var instance = new SingleInstance();
        if (!instance.IsPrimary)
        {
            if (SingleInstance.TrySend(paths))
                return 0;

            MessageBox.Show(Strings.AnotherInstanceUnreachable, "Nimbus", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }

        LegacyMigration.Run();

        var app = new App { InitialPaths = paths, Instance = instance };
        app.InitializeComponent();
        return app.Run();
    }
}
