using System.Windows;
using Wpf.Ui.Controls;

namespace Nimbus;

public partial class SignInWindow : FluentWindow
{
    private readonly Func<Task<bool>> _signIn;

    public SignInWindow(Func<Task<bool>> signIn)
    {
        _signIn = signIn;
        InitializeComponent();
    }

    private async void OnSignInClick(object sender, RoutedEventArgs e)
    {
        SignInButton.IsEnabled = false;
        SignInButton.Content = Strings.SignInWaiting;

        // Başarılı girişte pencereyi App kapatır; hata bildirimle gösterilir.
        await _signIn();

        SignInButton.Content = Strings.SignInButton;
        SignInButton.IsEnabled = true;
    }
}
