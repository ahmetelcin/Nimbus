using System.Globalization;
using System.Windows.Markup;

namespace Nimbus;

internal static class Localization
{
    // Değiştirilmeden önceki değer Windows'un görüntü dilidir.
    private static readonly CultureInfo WindowsCulture = CultureInfo.CurrentUICulture;

    public static void Apply() => Strings.Culture = Resolve(AppSettings.Load().Language, WindowsCulture);

    // Çalışan arka plan görevleri eski iş parçacığı kültürünü taşır; bu yüzden Strings.Culture kullanılır.
    internal static CultureInfo Resolve(string? language, CultureInfo windows) =>
        new(language ?? (windows.TwoLetterISOLanguageName == "tr" ? "tr" : "en"));
}

// XAML'de: Text="{local:Loc AnahtarAdi}"
public sealed class Loc(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        Strings.ResourceManager.GetString(key, Strings.Culture) ?? key;
}
