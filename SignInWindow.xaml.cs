using System.Windows;
using Wpf.Ui.Controls;

namespace DriveAc;

public partial class SignInWindow : FluentWindow
{
    private const string SignInText = "Google ile giriş yap";

    private readonly Func<Task<bool>> _signIn;

    public SignInWindow(Func<Task<bool>> signIn)
    {
        _signIn = signIn;
        InitializeComponent();
    }

    private async void OnSignInClick(object sender, RoutedEventArgs e)
    {
        SignInButton.IsEnabled = false;
        SignInButton.Content = "Tarayıcıda giriş bekleniyor…";

        // Başarılı girişte pencereyi App kapatır; hata bildirimle gösterilir.
        await _signIn();

        SignInButton.Content = SignInText;
        SignInButton.IsEnabled = true;
    }
}
