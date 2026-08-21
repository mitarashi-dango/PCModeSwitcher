using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace PCModeSwitcher.Services;

public static class AppLanguages
{
    public const string System = "system";
    public const string Japanese = "ja-JP";
    public const string English = "en-US";
    public const string SimplifiedChinese = "zh-Hans";
    public const string TraditionalChinese = "zh-Hant";
    public const string Spanish = "es-ES";
    public const string Esperanto = "eo";
    public const string Arabic = "ar-SA";
    public const string Hindi = "hi-IN";

    public static readonly string[] All =
        [System, Japanese, English, SimplifiedChinese, TraditionalChinese, Spanish, Arabic, Hindi, Esperanto];
}

public sealed class LocalizationService : INotifyPropertyChanged
{
    private static readonly CultureInfo SystemUiCulture = CultureInfo.CurrentUICulture;
    private static readonly Dictionary<string, string> Japanese = LocalizationStrings.Create(0);
    private static readonly Dictionary<string, string> English = LocalizationStrings.Create(1);
    private static readonly Dictionary<string, string> TraditionalChinese = LocalizationStrings.Create(2);
    private static readonly Dictionary<string, string> SimplifiedChinese = LocalizationStrings.Create(3);
    private static readonly Dictionary<string, string> Spanish = LocalizationStrings.Create(4);
    private static readonly Dictionary<string, string> Esperanto = LocalizationStrings.Create(5);
    private static readonly Dictionary<string, string> Arabic = LocalizationStrings.Create(6);
    private static readonly Dictionary<string, string> Hindi = LocalizationStrings.Create(7);
    private IReadOnlyDictionary<string, string> _strings = Japanese;
    private string _languageSetting = AppLanguages.System;

    public static LocalizationService Current { get; } = new();
    public static event EventHandler? LanguageChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public string LanguageSetting => _languageSetting;
    public string ResolvedLanguage { get; private set; } = AppLanguages.Japanese;
    public FlowDirection FlowDirection =>
        ResolvedLanguage == AppLanguages.Arabic
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
    public string this[string key] => Get(key);

    public static bool IsSupported(string? language) =>
        language is not null && AppLanguages.All.Contains(language, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string? language) =>
        IsSupported(language)
            ? AppLanguages.All.First(value => string.Equals(value, language, StringComparison.OrdinalIgnoreCase))
            : AppLanguages.System;

    public static void SetLanguage(string? language)
    {
        var normalized = Normalize(language);
        var resolved = normalized == AppLanguages.System
            ? ResolveSystemLanguage(SystemUiCulture)
            : normalized;
        if (Current._languageSetting == normalized && Current.ResolvedLanguage == resolved)
            return;

        Current._languageSetting = normalized;
        Current.ResolvedLanguage = resolved;
        Current._strings = resolved switch
        {
            AppLanguages.English => English,
            AppLanguages.SimplifiedChinese => SimplifiedChinese,
            AppLanguages.TraditionalChinese => TraditionalChinese,
            AppLanguages.Spanish => Spanish,
            AppLanguages.Esperanto => Esperanto,
            AppLanguages.Arabic => Arabic,
            AppLanguages.Hindi => Hindi,
            _ => Japanese
        };

        var culture = CultureInfo.GetCultureInfo(resolved);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Current.PropertyChanged?.Invoke(Current, new PropertyChangedEventArgs(Binding.IndexerName));
        Current.PropertyChanged?.Invoke(Current, new PropertyChangedEventArgs(nameof(LanguageSetting)));
        Current.PropertyChanged?.Invoke(Current, new PropertyChangedEventArgs(nameof(FlowDirection)));
        LanguageChanged?.Invoke(Current, EventArgs.Empty);
    }

    public static string Get(string key) =>
        Current._strings.TryGetValue(key, out var value)
            ? value
            : Japanese.TryGetValue(key, out var japanese) ? japanese : key;

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentUICulture, Get(key), args);

    public static string Translate(string text)
    {
        if (string.IsNullOrEmpty(text) || Current.ResolvedLanguage == AppLanguages.Japanese)
            return text;
        return Current._strings.TryGetValue("Message." + text, out var translated)
            ? translated
            : text;
    }

    internal static string ResolveSystemLanguage(CultureInfo culture)
    {
        if (culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return culture.Name.Contains("Hant", StringComparison.OrdinalIgnoreCase) ||
                   culture.Name.Equals("zh-TW", StringComparison.OrdinalIgnoreCase) ||
                   culture.Name.Equals("zh-HK", StringComparison.OrdinalIgnoreCase) ||
                   culture.Name.Equals("zh-MO", StringComparison.OrdinalIgnoreCase)
                ? AppLanguages.TraditionalChinese
                : AppLanguages.SimplifiedChinese;
        if (culture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase))
            return AppLanguages.English;
        if (culture.TwoLetterISOLanguageName.Equals("es", StringComparison.OrdinalIgnoreCase))
            return AppLanguages.Spanish;
        if (culture.TwoLetterISOLanguageName.Equals("ar", StringComparison.OrdinalIgnoreCase))
            return AppLanguages.Arabic;
        if (culture.TwoLetterISOLanguageName.Equals("hi", StringComparison.OrdinalIgnoreCase))
            return AppLanguages.Hindi;
        return AppLanguages.Japanese;
    }
}

[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension(string key) => Key = key;
    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]")
        {
            Source = LocalizationService.Current,
            Mode = BindingMode.OneWay
        }.ProvideValue(serviceProvider);
}
