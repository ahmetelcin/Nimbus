using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Nimbus;

internal enum ConflictChoice
{
    TakeGoogle,
    KeepLocal,
    KeepBoth,
}

internal sealed class Notifier
{
    private const string AppTitle = "Nimbus";
    private const string AppUserModelId = "Nimbus.App";
    private const string Group = "nimbus";
    private const string ProgressTag = "upload";

    private readonly ToastNotifier _notifier;
    private readonly ConcurrentDictionary<string, ToastNotification> _conflictToasts = new();
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

    // Aynı belge için önceki bildirimin yerine geçer; düzenleme sürerken bildirimler birikmez.
    public void ShowQuietInfo(string key, string title, string message)
    {
        var toast = CreateTextToast(title, message, silent: true);
        toast.Tag = TagFor("saved", key);
        TryShow(toast);
    }

    // onChoice başka bir iş parçacığından çağrılabilir.
    public void ShowConflict(string key, string fileName, Action<ConflictChoice> onChoice)
    {
        var xml = new XmlDocument();
        // reminder senaryosu, kullanıcı seçene kadar bildirimin ekranda kalmasını sağlar.
        xml.LoadXml($"""
            <toast scenario="reminder">
              <visual>
                <binding template="ToastGeneric">
                  <text>{SecurityElement.Escape($"{fileName} iki yerde de değişti")}</text>
                  <text>Hem Google'da hem bilgisayarda kaydedilmemiş değişiklik var. Hiçbir şey otomatik yazılmadı.</text>
                </binding>
              </visual>
              <actions>
                <action activationType="foreground" arguments="{ConflictChoice.TakeGoogle}" content="Google'dakini al" />
                <action activationType="foreground" arguments="{ConflictChoice.KeepLocal}" content="Yerelini koru" />
                <action activationType="foreground" arguments="{ConflictChoice.KeepBoth}" content="İkisini de tut" />
              </actions>
            </toast>
            """);

        var toast = new ToastNotification(xml) { Tag = TagFor("conflict", key), Group = Group };
        toast.Activated += (_, args) =>
        {
            _conflictToasts.TryRemove(key, out ToastNotification? _);
            if (args is ToastActivatedEventArgs { Arguments: var arguments } && Enum.TryParse<ConflictChoice>(arguments, out var choice))
                onChoice(choice);
        };

        // Olay yalnızca nesne yaşadığı sürece gelir.
        _conflictToasts[key] = toast;
        TryShow(toast);
    }

    public void ShowError(string message)
    {
        if (!AreEnabled() || !TryShow(CreateTextToast("Nimbus: işlem tamamlanamadı", message)))
            MessageBox.Show(message, AppTitle, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public void ShowProgress(string fileName) =>
        ShowProgressToast(fileName, $"Yükleniyor: {fileName}", CreateProgressData(0));

    public void ShowDownload(string fileName) =>
        ShowProgressToast(fileName, $"Google'dan alınıyor: {fileName}", CreateProgressData(null, "İndiriliyor…"));

    private void ShowProgressToast(string fileName, string title, NotificationData data)
    {
        _uploadingFile = fileName;
        _sequence = 0;
        data.SequenceNumber = ++_sequence;

        var xml = new XmlDocument();
        // reminder senaryosu, bildirimin yükleme bitene kadar ekranda kalmasını sağlar.
        xml.LoadXml($"""
            <toast scenario="reminder">
              <visual>
                <binding template="ToastGeneric">
                  <text>{SecurityElement.Escape(title)}</text>
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
            Data = data,
        };
        TryShow(toast);
        StatusChanged?.Invoke(title);
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

    private NotificationData CreateProgressData(double? fraction, string? status = null)
    {
        var data = new NotificationData { SequenceNumber = ++_sequence };
        // Değer ondalık noktayla yazılmalı; Türkçe kültürdeki virgül çubuğu bozar.
        data.Values["progressValue"] = fraction?.ToString("0.###", CultureInfo.InvariantCulture) ?? "indeterminate";
        data.Values["progressText"] = fraction is { } f ? $"%{Math.Round(f * 100)}" : " ";
        data.Values["progressStatus"] = status ?? (fraction is null ? "Dönüştürülüyor…" : "Yükleniyor…");
        return data;
    }

    private static string TagFor(string kind, string key) => $"{kind}-{key.GetHashCode():X8}";

    private static ToastNotification CreateTextToast(string title, string message, bool silent = false)
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
              {(silent ? """<audio silent="true" />""" : "")}
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
        using (var icon = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Nimbus.png")).Stream)
        using (var file = File.Create(AppPaths.NotificationIconFile))
            icon.CopyTo(file);

        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppUserModelId}");
        key.SetValue("DisplayName", AppTitle);
        key.SetValue("IconUri", AppPaths.NotificationIconFile);
    }
}
