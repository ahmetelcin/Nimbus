using System.Windows;
using Wpf.Ui.Controls;

namespace Nimbus;

public partial class ReopenWindow : FluentWindow
{
    public ReopenWindow(string fileName)
    {
        InitializeComponent();
        MessageText.Text = string.Format(Strings.ReopenMessage, fileName);
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
