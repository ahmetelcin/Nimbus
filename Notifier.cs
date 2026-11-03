using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace DriveAc;

internal sealed class Notifier
{
    private const string AppTitle = "DriveAç";
    private const string AppUserModelId = "DriveAc.App";
    private const string Group = "driveac";
    private const string ProgressTag = "upload";

    private readonly ToastNotifier _notifier;
    private string? _uploadingFile;
    private uint _sequence;

    public Notifier()
    {
        Register();
        _notifier = ToastNotificationManager.CreateToastNotifier(AppUserModelId);
    }

    public event Action<string?>? StatusChanged;

    public void ShowInfo(string title, string message) =>
        TryShow(CreateTextToast(title, message));

    public void ShowError(string message)
    {
        if (!AreEnabled() || !TryShow(CreateTextToast("DriveAç: işlem tamamlanamadı", message)))
            MessageBox.Show(message, AppTitle, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public void ShowProgress(string fileName)
    {
        _uploadingFile = fileName;
        _sequence = 0;

        var xml = new XmlDocument();
        // reminder senaryosu, bildirimin yükleme bitene kadar ekranda kalmasını sağlar.
        xml.LoadXml($"""
            <toast scenario="reminder">
              <visual>
                <binding template="ToastGeneric">
                  <text>{SecurityElement.Escape($"Yükleniyor: {fileName}")}</text>
                  <progress value="{"{progressValue}"}" valueStringOverride="{"{progressText}"}" status="{"{progressStatus}"}" />
                </binding>
              </visual>
              <actions>
                <action activationType="system" arguments="dismiss" content="Gizle" />
              </actions>
              <audio silent="true" />
            </toast>
            """);

        var toast = new ToastNotification(xml)
        {
            Tag = ProgressTag,
            Group = Group,
            Data = CreateProgressData(0),
        };
        TryShow(toast);
        StatusChanged?.Invoke($"Yükleniyor: {fileName}");
    }

    // Yükleme iş parçacığından çağrılabilir.
    public void UpdateProgress(double? fraction)
    {
        if (_uploadingFile is not { } fileName)
            return;

        try
        {
            _notifier.Update(CreateProgressData(fraction), ProgressTag, Group);
        }
        catch (COMException)
        {
            // İlerleme bilgisi gösterilemezse yükleme yine sürer.
        }

        StatusChanged?.Invoke(fraction is { } f
            ? $"Yükleniyor: {fileName} (%{Math.Round(f * 100)})"
            : $"Dönüştürülüyor: {fileName}");
    }

    public void ClearProgress()
    {
        if (_uploadingFile is null)
            return;

        _uploadingFile = null;
        try
        {
            ToastNotificationManager.History.Remove(ProgressTag, Group, AppUserModelId);
        }
        catch (COMException)
        {
        }

        StatusChanged?.Invoke(null);
    }

    private NotificationData CreateProgressData(double? fraction)
    {
        var data = new NotificationData { SequenceNumber = ++_sequence };
        // Değer ondalık noktayla yazılmalı; Türkçe kültürdeki virgül çubuğu bozar.
        data.Values["progressValue"] = fraction?.ToString("0.###", CultureInfo.InvariantCulture) ?? "indeterminate";
        data.Values["progressText"] = fraction is { } f ? $"%{Math.Round(f * 100)}" : " ";
        data.Values["progressStatus"] = fraction is null ? "Dönüştürülüyor…" : "Yükleniyor…";
        return data;
    }

    private static ToastNotification CreateTextToast(string title, string message)
    {
        var xml = new XmlDocument();
        xml.LoadXml($"""
            <toast>
              <visual>
                <binding template="ToastGeneric">
                  <text>{SecurityElement.Escape(title)}</text>
                  <text>{SecurityElement.Escape(message)}</text>
                </binding>
              </visual>
            </toast>
            """);
        return new ToastNotification(xml) { Group = Group };
    }

    private bool TryShow(ToastNotification toast)
    {
        try
        {
            _notifier.Show(toast);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
    }

    private bool AreEnabled()
    {
        try
        {
            return _notifier.Setting == NotificationSetting.Enabled;
        }
        catch (COMException)
        {
            // İlk bildirimden önce Windows uygulamayı tanımaz (0x80070490); bu "kapalı" demek değildir.
            return true;
        }
    }

    // Paketlenmemiş uygulamalar toast için kimliklerini kayıt defterinde tanımlar.
    private static void Register()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.NotificationIconFile)!);
        using (var icon = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/DriveAc.png")).Stream)
        using (var file = File.Create(AppPaths.NotificationIconFile))
            icon.CopyTo(file);

        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppUserModelId}");
        key.SetValue("DisplayName", AppTitle);
        key.SetValue("IconUri", AppPaths.NotificationIconFile);
    }
}
