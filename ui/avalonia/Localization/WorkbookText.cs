using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace CyanCalc.Controls;

/// <summary>One display language for a workbook, its shell and detached popups.
/// Original app/component translations are embedded from the pinned source.
/// Native-only wording has its own explicitly named resource namespace.</summary>
public class WorkbookText : INotifyPropertyChanged
{
    private static readonly Dictionary<string, Dictionary<string, string>> Locales = Load();
    private string _language = "en-US";
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Language
    {
        get => _language;
        set
        {
            if (_language == value) return;
            _language = value;
            PropertyChanged?.Invoke(this, new(nameof(Language)));
        }
    }
    public string this[string key] => Lookup(Language, key);
    public LocalizedText Key(string key, params (string Name, object? Value)[] values) => new(this, key, values);
    public LocalizedText Compose(Func<WorkbookText, string> render) => new(this, render);
    public string Format(string key, string parameter, object value) => Key(key, (parameter, value)).Value;
    internal static string Lookup(string language, string key)
    {
        var locale = language.Split('-', '_')[0].ToLowerInvariant();
        if (Locales.TryGetValue(locale, out var strings) && strings.TryGetValue(key, out var value)) return value;
        return Locales["en"].TryGetValue(key, out value) ? value : throw new ArgumentException("Unknown UI translation key: " + key);
    }
    internal static IEnumerable<string> Keys => Locales["en"].Keys;
    internal static IReadOnlyDictionary<string, string> ForLanguage(string language) => Locales[language];
    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        var assembly = typeof(WorkbookText).Assembly;
        var locales = new Dictionary<string, Dictionary<string, string>>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.Contains(".Locales.")))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var json = JsonDocument.Parse(stream);
            var locale = name.Split('.')[^2].Split('_')[0];
            if (!locales.TryGetValue(locale, out var strings)) locales[locale] = strings = new();
            Flatten(json.RootElement, "", strings);
        }
        return locales;
    }
    private static void Flatten(JsonElement value, string prefix, Dictionary<string, string> target)
    {
        foreach (var property in value.EnumerateObject())
        {
            var key = prefix + property.Name;
            if (property.Value.ValueKind == JsonValueKind.Object) Flatten(property.Value, key + ".", target);
            else target.Add(key, property.Value.GetString()!);
        }
    }
}

/// <summary>A translated UI label, distinct from user data and engine identifiers.
/// Binding changes only the label, retaining the control, selection and draft text.</summary>
public sealed class LocalizedText
{
    private readonly WorkbookText _text;
    private readonly Func<WorkbookText, string> _render;
    internal string? ResourceKey { get; }
    internal LocalizedText(WorkbookText text, string key, (string Name, object? Value)[] values)
    {
        _text = text; ResourceKey = key;
        _render = source => values.Aggregate(source[key], (result, pair) => result.Replace("{{" + pair.Name + "}}",
            pair.Value is LocalizedText translated ? translated.Value : Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? ""));
    }
    internal LocalizedText(WorkbookText text, Func<WorkbookText, string> render) { _text = text; _render = render; }
    public string Value => _render(_text);
    public T Bind<T>(T target, AvaloniaProperty property) where T : AvaloniaObject
    {
        BindTo(target, property);
        return target;
    }
    /// <summary>For transient messages, dispose this binding before replacing it with literal text.</summary>
    public IDisposable BindTo(AvaloniaObject target, AvaloniaProperty property) =>
        target.Bind(property, new Binding(nameof(WorkbookText.Language)) { Source = _text, Mode = BindingMode.OneWay,
            Converter = new FuncValueConverter<string, string>(_ => Value) });
    public T Describe<T>(T target) where T : Control
    {
        Bind(target, AutomationProperties.NameProperty); Bind(target, ToolTip.TipProperty); return target;
    }
    public TextBlock Block() => Bind(new TextBlock(), TextBlock.TextProperty);
    internal static IDataTemplate Options(Func<string, LocalizedText> label) =>
        new FuncDataTemplate<string>((value, _) => label(value ?? "").Block());
}
