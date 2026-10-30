using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace CyanCalc.Controls;

public sealed record LinkEditorResult(JsonElement? Link, string Label, bool Delete = false);

/// <summary>Editor for worksheet link metadata; HYPERLINK formulas are edited in the cell editor.</summary>
public sealed class LinkEditorWindow : Window
{
    private static readonly string[] AllowedSchemes = ["http", "https", "ftp", "ftps", "file", "mailto"];
    private static readonly Regex Domain = new(@"^[\w-]+(\.[\w-]+)+([/?#]\S*)?$", RegexOptions.CultureInvariant);
    private static readonly Regex Email = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant);
    private static readonly Regex DefinedNamePattern = new(@"^[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.CultureInvariant);
    private static readonly Regex CellReferencePattern = new(@"^\$?[A-Za-z]{1,3}\$?[0-9]{1,7}(:\$?[A-Za-z]{1,3}\$?[0-9]{1,7})?$", RegexOptions.CultureInvariant);
    private readonly WorkbookText _text;
    private readonly TabControl _kind = new() { Name = "LinkKind", SelectedIndex = 0 };
    private readonly TextBox _url = new() { Name = "LinkUrl" };
    private readonly ComboBox _sheet;
    private readonly TextBox _reference = new() { Name = "LinkReference" };
    private readonly TextBox _email = new() { Name = "LinkEmail" };
    private readonly TextBox _subject = new() { Name = "LinkSubject" };
    private readonly TextBox _body = new() { Name = "LinkBody" };
    private readonly TextBox _tooltip = new() { Name = "LinkHint" };
    private readonly TextBox _label = new() { Name = "LinkLabel" };
    private readonly TextBlock _error = new() { Name = "LinkError" };
    private readonly Button _save = new WorkbookButton() { Name = "LinkSave" };
    private readonly StackPanel _urlGroup = new() { Spacing = 8 };
    private readonly StackPanel _documentGroup = new() { Spacing = 8 };
    private readonly StackPanel _emailGroup = new() { Spacing = 8 };
    private readonly bool _hasLink;
    private readonly int _initialKind;
    private string _emailExtra = "";
    private IDisposable? _errorTranslation;

    public LinkEditorWindow(string[] sheetNames, string selectedSheet, JsonElement? initialLink, string initialLabel, WorkbookText? text = null)
    {
        _text = text ?? new WorkbookText();
        _hasLink = initialLink is not null;
        _text.Key(_hasLink ? "link_dialog.title_edit" : "link_dialog.title_add").Bind(this, Window.TitleProperty);
        _kind.ItemsSource = new[] { "external_tab", "document_tab", "email_tab" }.Select(key =>
        {
            var label = _text.Key("link_dialog." + key).Block();
            return _text.Key("link_dialog." + key).Bind(new TabItem { Header = label }, AutomationProperties.NameProperty);
        }).ToArray();
        _kind.Classes.Add("dialog-tabs");
        WorkbookDialog.StyleButton(_save, _text.Key("link_dialog.save"), "primary", "Check");
        _sheet = new ComboBox { Name = "LinkSheet", ItemsSource = sheetNames, SelectedItem = selectedSheet };
        _sheet.Classes.Add("dialog-select");
        _text.Key("native.worksheet").Bind(_sheet, AutomationProperties.NameProperty);
        _label.Text = initialLabel;
        _text.Key("link_dialog.url_placeholder").Bind(_url, TextBox.WatermarkProperty);
        _text.Key("link_dialog.cell_reference_placeholder").Bind(_reference, TextBox.WatermarkProperty);
        _text.Key("link_dialog.email_placeholder").Bind(_email, TextBox.WatermarkProperty);
        _text.Key("link_dialog.subject_placeholder").Bind(_subject, TextBox.WatermarkProperty);
        _text.Key("link_dialog.body_placeholder").Bind(_body, TextBox.WatermarkProperty);
        _text.Key("link_dialog.tooltip_placeholder").Bind(_tooltip, TextBox.WatermarkProperty);
        _text.Key("link_dialog.label_placeholder").Bind(_label, TextBox.WatermarkProperty);

        if (initialLink is { } link)
        {
            _tooltip.Text = link.TryGetProperty("tooltip", out var tip) && tip.ValueKind == JsonValueKind.String ? tip.GetString() : "";
            if (link.GetProperty("type").GetString() == "Internal")
            {
                _kind.SelectedIndex = 1;
                var location = link.GetProperty("location").GetString() ?? "";
                var separator = location.LastIndexOf('!');
                if (separator >= 0)
                {
                    var sheet = location[..separator];
                    if (sheet.Length >= 2 && sheet[0] == '\'' && sheet[^1] == '\'') sheet = sheet[1..^1];
                    _sheet.SelectedItem = sheet;
                    _reference.Text = location[(separator + 1)..].Replace("$", "");
                }
                else _reference.Text = location;
            }
            else
            {
                var target = link.GetProperty("target").GetString() ?? "";
                if (target.StartsWith("mailto:", StringComparison.Ordinal))
                {
                    _kind.SelectedIndex = 2;
                    ParseMailto(target, out var address, out var subject, out var body, out _emailExtra);
                    _email.Text = address; _subject.Text = subject; _body.Text = body;
                }
                else _url.Text = target;
            }
        }
        _initialKind = _hasLink ? _kind.SelectedIndex : -1;
        foreach (var (input, key) in new[] { (_url, "url"), (_reference, "cell_reference"), (_email, "email"),
            (_subject, "subject"), (_body, "body"), (_tooltip, "tooltip"), (_label, "label") })
            WorkbookDialog.Input(input, _text.Key("link_dialog." + key + "_placeholder"));
        _urlGroup.Children.Add(_url);
        _documentGroup.Children.Add(_sheet); _documentGroup.Children.Add(_reference);
        _emailGroup.Children.Add(_email); _emailGroup.Children.Add(_subject); _emailGroup.Children.Add(_body);
        _error.FontSize = 12; _error.LineHeight = 16.8; _error.TextWrapping = TextWrapping.Wrap;
        _error.Margin = new Thickness(0, -2, 0, 0);
        _error.Bind(TextBlock.ForegroundProperty, _error.GetResourceObservable("Dialog-error-main"));
        var fields = WorkbookDialog.Body();
        fields.Children.Add(_kind);
        fields.Children.Add(_urlGroup); fields.Children.Add(_documentGroup); fields.Children.Add(_emailGroup);
        fields.Children.Add(_tooltip); fields.Children.Add(_label);
        var footer = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(12, 8) };
        if (_hasLink)
        {
            var remove = WorkbookDialog.Button(_text.Key("link_dialog.delete"),
                () => Close(new LinkEditorResult(null, _label.Text ?? "", true)), "ghost", "Trash2");
            remove.Name = "LinkDelete"; remove.Classes.Add("danger-text"); remove.HorizontalAlignment = HorizontalAlignment.Left;
            footer.Children.Add(remove);
        }
        _save.Click += (_, _) => { if (TryBuild(out var result, out _)) Close(result); };
        Grid.SetColumn(_save, 1); footer.Children.Add(_save);
        WorkbookDialog.Configure(this, _text.Key(_hasLink ? "link_dialog.title_edit" : "link_dialog.title_add"),
            _text.Key("link_dialog.close"), fields, footer, 380);
        _kind.SelectionChanged += (_, _) => UpdateValidation();
        foreach (var field in new[] { _url, _reference, _email, _subject, _body, _tooltip, _label }) field.TextChanged += (_, _) => UpdateValidation();
        _sheet.SelectionChanged += (_, _) => UpdateValidation();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(null); e.Handled = true; }
            else if (e.Key == Key.Enter && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0 && TryBuild(out var result, out var ignoredError)) { Close(result); e.Handled = true; }
        };
        Closed += (_, _) => _errorTranslation?.Dispose();
        UpdateValidation();
        Opened += (_, _) => (_kind.SelectedIndex switch { 1 => _reference, 2 => _email, _ => _url }).Focus();
    }

    private void UpdateValidation()
    {
        _urlGroup.IsVisible = _kind.SelectedIndex == 0;
        _documentGroup.IsVisible = _kind.SelectedIndex == 1;
        _sheet.IsEnabled = _kind.SelectedIndex == 1 && ValidCellRange((_reference.Text ?? "").Replace("$", "").Trim());
        _emailGroup.IsVisible = _kind.SelectedIndex == 2;
        _save.IsEnabled = TryBuild(out _, out var error);
        _urlGroup.Children.Remove(_error); _documentGroup.Children.Remove(_error); _emailGroup.Children.Remove(_error);
        var active = _kind.SelectedIndex switch { 1 => _reference, 2 => _email, _ => _url };
        _error.IsVisible = !_save.IsEnabled && !string.IsNullOrWhiteSpace(active.Text);
        foreach (var input in new[] { _url, _reference, _email }) input.Classes.Set("invalid", input == active && _error.IsVisible);
        if (_kind.SelectedIndex == 2) _emailGroup.Children.Insert(1, _error);
        else (_kind.SelectedIndex == 1 ? _documentGroup : _urlGroup).Children.Add(_error);
        _errorTranslation?.Dispose();
        _errorTranslation = _text.Compose(labels =>
        {
            var main = _kind.SelectedIndex switch { 0 => _url.Text, 1 => _reference.Text, _ => _email.Text };
            if (string.IsNullOrWhiteSpace(main)) return "";
            TryBuild(out _, out var message); return message;
        }).BindTo(_error, TextBlock.TextProperty);
    }

    public bool TryBuild(out LinkEditorResult? result, out string error)
    {
        result = null; error = "";
        var kind = _kind.SelectedIndex;
        var main = kind switch { 0 => _url.Text, 1 => _reference.Text, 2 => _email.Text, _ => null };
        if (_hasLink && kind == _initialKind && string.IsNullOrWhiteSpace(main))
        { result = new LinkEditorResult(null, _label.Text ?? "", true); return true; }
        if (kind == 0)
        {
            var target = NormalizeUrl(main ?? "");
            if (target is null) { error = _text["link_dialog.invalid_url"]; return false; }
            result = new LinkEditorResult(JsonSerializer.SerializeToElement(new { type = "External", target, tooltip = EmptyToNull(_tooltip.Text) }), _label.Text ?? "");
        }
        else if (kind == 1)
        {
            var reference = (_reference.Text ?? "").Replace("$", "").Trim();
            var isCells = ValidCellRange(reference);
            if (!isCells && !DefinedNamePattern.IsMatch(reference)) { error = _text["link_dialog.invalid_reference"]; return false; }
            var sheet = _sheet.SelectedItem as string;
            var location = isCells ? $"{ReferenceUtils.QuoteName(sheet ?? "")}!{reference.ToUpperInvariant()}" : reference;
            result = new LinkEditorResult(JsonSerializer.SerializeToElement(new { type = "Internal", location, tooltip = EmptyToNull(_tooltip.Text) }), _label.Text ?? "");
        }
        else if (kind == 2)
        {
            var email = _email.Text ?? "";
            if (!Email.IsMatch(email.Split('?')[0])) { error = _text["link_dialog.invalid_email"]; return false; }
            email = email.Trim();
            ParseMailto(email, out var address, out var inlineSubject, out var inlineBody, out var inlineExtra);
            var subject = string.IsNullOrWhiteSpace(_subject.Text) ? inlineSubject : _subject.Text.Trim();
            var body = string.IsNullOrWhiteSpace(_body.Text) ? inlineBody : _body.Text.Trim();
            var parameters = new List<string>();
            if (subject.Length > 0) parameters.Add("subject=" + EncodeURIComponent(subject));
            if (body.Length > 0) parameters.Add("body=" + EncodeURIComponent(body));
            if (inlineExtra.Length > 0) parameters.Add(inlineExtra);
            if (_emailExtra.Length > 0) parameters.Add(_emailExtra);
            var target = "mailto:" + address + (parameters.Count > 0 ? "?" + string.Join("&", parameters) : "");
            result = new LinkEditorResult(JsonSerializer.SerializeToElement(new { type = "External", target, tooltip = EmptyToNull(_tooltip.Text) }), _label.Text ?? "");
        }
        else { error = _text["native.choose_link_type"]; return false; }
        return true;
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string QuoteSheet(string sheet) => "'" + sheet.Replace("'", "''") + "'";
    private static bool ValidCellRange(string reference) => CellReferencePattern.IsMatch(reference);

    private static string EncodeURIComponent(string value) => Uri.EscapeDataString(value)
        .Replace("%21", "!").Replace("%27", "'").Replace("%28", "(").Replace("%29", ")").Replace("%2A", "*");

    public static string? NormalizeUrl(string input)
    {
        var value = input.Trim();
        if (value.Length == 0) return null;
        if (TryAbsoluteAllowedUri(value, out _)) return value;
        return Domain.IsMatch(value) && TryAbsoluteAllowedUri("https://" + value, out _) ? "https://" + value : null;
    }

    public static bool TryAbsoluteAllowedUri(string value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) && AllowedSchemes.Contains(parsed.Scheme, StringComparer.OrdinalIgnoreCase))
        { uri = parsed; return true; }
        uri = null!; return false;
    }

    public static string DisplayTarget(string target)
    {
        if (target.StartsWith("mailto:", StringComparison.Ordinal))
            return target[7..].Split('?')[0];
        return target;
    }

    public static string TooltipTarget(string target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri)) return target;
        if (uri.Scheme is "http" or "https") return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        return uri.Scheme == "mailto" ? DisplayTarget(target) : target;
    }

    private static void ParseMailto(string target, out string address, out string subject, out string body, out string others)
    {
        var rest = target.StartsWith("mailto:", StringComparison.Ordinal) ? target[7..] : target;
        var index = rest.IndexOf('?');
        address = index < 0 ? rest : rest[..index];
        subject = body = others = "";
        if (index < 0) return;
        var extra = new List<string>();
        foreach (var item in rest[(index + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            if (item.StartsWith("subject=", StringComparison.OrdinalIgnoreCase)) subject = Decode(item[8..]);
            else if (item.StartsWith("body=", StringComparison.OrdinalIgnoreCase)) body = Decode(item[5..]);
            else extra.Add(item);
        }
        others = string.Join("&", extra);
    }

    private static string Decode(string value)
    {
        try { return Uri.UnescapeDataString(value); }
        catch (UriFormatException) { return value; }
    }
}
