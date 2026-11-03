using System.Windows;
using Wpf.Ui.Controls;

namespace Nimbus;

public partial class ReopenWindow : FluentWindow
{
    public ReopenWindow(string fileName)
    {
        InitializeComponent();
        MessageText.Text = $"{fileName} Google'da düzenlenmiş ve bu değişiklikler bilgisayardaki dosyaya henüz kaydedilmemiş. Baştan başlarsanız eski kopya Drive'da kalır.";
    }

    internal ReopenChoice? Choice { get; private set; }

    internal bool DontAskAgain => DontAskAgainBox.IsChecked == true;

    private void OnContinueClick(object sender, RoutedEventArgs e) => Finish(ReopenChoice.Continue);

    private void OnStartOverClick(object sender, RoutedEventArgs e) => Finish(ReopenChoice.StartOver);

    private void Finish(ReopenChoice choice)
    {
        Choice = choice;
        Close();
    }
}
