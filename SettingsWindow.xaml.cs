using System.Windows;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace Nimbus;

public partial class SettingsWindow : FluentWindow
{
    private readonly Action _linkFileTypes;
    private readonly Action _unlinkFileTypes;

    public SettingsWindow(Action linkFileTypes, Action unlinkFileTypes)
    {
        _linkFileTypes = linkFileTypes;
        _unlinkFileTypes = unlinkFileTypes;
        InitializeComponent();

        var settings = AppSettings.Load();
        ShowBrowser(settings.BrowserPath);
        CleanupDaysBox.Value = settings.CleanupDays;
        AutoStartSwitch.IsChecked = AutoStart.IsEnabled;
        AutoSaveSwitch.IsChecked = settings.AutoSaveBack;
        ReopenChoiceBox.SelectedIndex = (int)settings.ReopenChoice;
        ExplorerMenuSwitch.IsChecked = settings.ExplorerMenu;

        UpdateModeBox.SelectedIndex = (int)settings.UpdateMode;
        UpdateText.Text = UpdateInstaller.CanSelfUpdate
            ? $"Sürüm {UpdateInstaller.CurrentVersion}. Yalnızca imzası doğrulanan sürümler kurulur."
            : $"Sürüm {UpdateInstaller.CurrentVersion}. Geliştirme derlemesi kendini güncellemez.";

        foreach (var toggle in new[] { ProgressSwitch, OpenedSwitch, AutoSavedSwitch, LinkCopiedSwitch })
            toggle.IsChecked = !settings.DisabledNotifications.Contains(NotificationOf(toggle));
    }

    private static OptionalNotification NotificationOf(ToggleSwitch toggle) => Enum.Parse<OptionalNotification>((string)toggle.Tag);

    private void ShowBrowser(string? path)
    {
        BrowserText.Text = path is null ? "Windows varsayılan tarayıcısı" : Path.GetFileNameWithoutExtension(path);
        DefaultBrowserButton.IsEnabled = path is not null;
    }

    private void SaveBrowser(string? path)
    {
        var settings = AppSettings.Load();
        settings.BrowserPath = path;
        settings.Save();
        ShowBrowser(path);
    }

    private void OnChooseBrowserClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Tarayıcı seçin", Filter = "Uygulamalar (*.exe)|*.exe" };
        if (dialog.ShowDialog(this) == true)
            SaveBrowser(dialog.FileName);
    }

    private void OnDefaultBrowserClick(object sender, RoutedEventArgs e) => SaveBrowser(null);

    private void OnCleanupDaysChanged(object sender, NumberBoxValueChangedEventArgs e)
    {
        // Kutu boşaltıldığında son geçerli değer korunur.
        if (e.NewValue is not { } days)
            return;

        var settings = AppSettings.Load();
        settings.CleanupDays = (int)days;
        settings.Save();
    }

    private void OnAutoStartClick(object sender, RoutedEventArgs e) =>
        AutoStart.SetEnabled(AutoStartSwitch.IsChecked == true);

    private void OnAutoSaveClick(object sender, RoutedEventArgs e)
    {
        var settings = AppSettings.Load();
        settings.AutoSaveBack = AutoSaveSwitch.IsChecked == true;
        settings.Save();
    }

    private void OnReopenChoiceChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var settings = AppSettings.Load();
        settings.ReopenChoice = (ReopenChoice)ReopenChoiceBox.SelectedIndex;
        settings.Save();
    }

    private void OnExplorerMenuClick(object sender, RoutedEventArgs e)
    {
        var enabled = ExplorerMenuSwitch.IsChecked == true;
        var settings = AppSettings.Load();
        settings.ExplorerMenu = enabled;
        settings.Save();
        ExplorerMenu.SetEnabled(enabled);
    }

    private void OnUpdateModeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var settings = AppSettings.Load();
        settings.UpdateMode = (UpdateMode)UpdateModeBox.SelectedIndex;
        settings.Save();
    }

    private void OnNotificationClick(object sender, RoutedEventArgs e)
    {
        var toggle = (ToggleSwitch)sender;
        var settings = AppSettings.Load();
        if (toggle.IsChecked == true)
            settings.DisabledNotifications.Remove(NotificationOf(toggle));
        else
            settings.DisabledNotifications.Add(NotificationOf(toggle));
        settings.Save();
    }

    private void OnLinkFileTypesClick(object sender, RoutedEventArgs e) => _linkFileTypes();

    private void OnUnlinkFileTypesClick(object sender, RoutedEventArgs e) => _unlinkFileTypes();
}
