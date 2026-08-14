using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace PCModeSwitcher.Services;

public static class AppLanguages
{
    public const string System = "system";
    public const string Japanese = "ja-JP";
    public const string English = "en-US";
    public const string TraditionalChinese = "zh-Hant";

    public static readonly string[] All = [System, Japanese, English, TraditionalChinese];
}

public sealed class LocalizationService : INotifyPropertyChanged
{
    private static readonly CultureInfo SystemUiCulture = CultureInfo.CurrentUICulture;
    private static readonly Dictionary<string, string> Japanese = LocalizationStrings.Create(0);
    private static readonly Dictionary<string, string> English = LocalizationStrings.Create(1);
    private static readonly Dictionary<string, string> TraditionalChinese = LocalizationStrings.Create(2);
    private IReadOnlyDictionary<string, string> _strings = Japanese;
    private string _languageSetting = AppLanguages.System;

    public static LocalizationService Current { get; } = new();
    public static event EventHandler? LanguageChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public string LanguageSetting => _languageSetting;
    public string ResolvedLanguage { get; private set; } = AppLanguages.Japanese;
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
            AppLanguages.TraditionalChinese => TraditionalChinese,
            _ => Japanese
        };

        var culture = CultureInfo.GetCultureInfo(resolved);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Current.PropertyChanged?.Invoke(Current, new PropertyChangedEventArgs(Binding.IndexerName));
        Current.PropertyChanged?.Invoke(Current, new PropertyChangedEventArgs(nameof(LanguageSetting)));
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

    private static string ResolveSystemLanguage(CultureInfo culture)
    {
        if (culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) &&
            !culture.Name.Contains("Hans", StringComparison.OrdinalIgnoreCase) &&
            !culture.Name.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) &&
            !culture.Name.Equals("zh-SG", StringComparison.OrdinalIgnoreCase))
            return AppLanguages.TraditionalChinese;
        if (culture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase))
            return AppLanguages.English;
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
