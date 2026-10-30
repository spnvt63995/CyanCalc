using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Layout;
using Avalonia.Threading;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;

namespace CyanCalc.Controls;

// Same function catalogue and engine completion grammar as FormulaHelper/formulaCompletion.ts.
internal sealed class FormulaHelper
{
    internal Action<Uri>? OpenLink { get; set; }
    private static bool _collapsed;
    private readonly Border _border;
    private readonly Popup _popup = new() { Placement = PlacementMode.BottomEdgeAlignedLeft, IsLightDismissEnabled = false };
    private static readonly Dictionary<string, JsonElement> Functions = Load();
    private string[] _matches = [];
    private int _selected;
    private int _replaceFrom;
    private string _prefix = "";
    private CellEditor? _editor;
    private string? _lastText;
    private int _lastCursor = -1;
    private readonly HashSet<CellEditor> _dismissedEditors = [];
    private readonly StackPanel _content = new();
    private static Dictionary<string, JsonElement> Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(x => x.EndsWith("functions.json")))!;
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(stream)!;
    }
    public FormulaHelper(WorkbookText? labels = null)
    {
        _border = new Border { Child = _content, Background = Brushes.White, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true };
        WorkbookChrome.Attach(_border);
        TextElement.SetFontFamily(_border, WorkbookDialog.Font); TextElement.SetFontSize(_border, 12);
        _popup.Child = _border;
    }
    public void Close() { _popup.IsOpen = false; _matches = []; }
    public void Reset()
    {
        Close(); _dismissedEditors.Clear();
        _editor = null; _lastText = null; _lastCursor = -1; _selected = 0; _prefix = "";
    }
    public void Update(UserModel session, EditingCell editing, CellEditor editor)
    {
        var text = editor.Text ?? "";
        // Like each original Editor, keep Escape dismissal until the shared text
        // changes. Caret/focus changes reset the highlighted candidate, not dismissal.
        if (text != _lastText) _dismissedEditors.Clear();
        if (text != _lastText || editor.CaretIndex != _lastCursor || !ReferenceEquals(editor, _editor)) _selected = 0;
        _lastText = text; _lastCursor = editor.CaretIndex; _editor = editor;
        if (_dismissedEditors.Contains(editor)) { Close(); return; }
        if (!text.StartsWith('=')) { Close(); return; }
        JsonElement context;
        try { context = session.Query("completion", new { sheet = editing.Sheet, row = editing.Row, column = editing.Column, formula = text, cursor = WorkbookView.ScalarOffset(text, editor.CaretIndex) }); }
        catch (IronCalcException) { Close(); return; }
        _matches = [];
        foreach (var expected in context.GetProperty("expecting").EnumerateArray())
        {
            if (expected.ValueKind != JsonValueKind.Object) continue;
            if (expected.TryGetProperty("Argument", out var argument) && Functions.TryGetValue(argument[0].GetString()!.ToLowerInvariant(), out var function))
            {
                RenderDetail(argument[0].GetString()!, argument[1].GetInt32() - 1, function);
                Show(editor); return;
            }
        }
        foreach (var expected in context.GetProperty("expecting").EnumerateArray())
        {
            if (expected.ValueKind != JsonValueKind.Object || !expected.TryGetProperty("FunctionName", out var name)) continue;
            var prefix = name.GetString()!.ToUpperInvariant();
            if (prefix != _prefix) _selected = 0;
            _prefix = prefix;
            _matches = Functions.Keys.Select(x => x.ToUpperInvariant()).Where(x => x.StartsWith(prefix, StringComparison.Ordinal)).Order().ToArray();
            _replaceFrom = context.GetProperty("replace_from").GetInt32() + 1;
            if (_matches.Length == 0) { Close(); return; }
            _selected = Math.Clamp(_selected, 0, _matches.Length - 1);
            RenderMatches(); Show(editor); return;
        }
        Close();
    }
    private void Show(CellEditor editor)
    {
        _editor = editor; _popup.PlacementTarget = editor;
        var palette = SheetPalette.For(editor.ActualThemeVariant);
        _border.Background = palette.Surface; _border.BorderBrush = palette.Token("grey-300");
        _border.MaxWidth = (TopLevel.GetTopLevel(editor)?.ClientSize.Width ?? 1000) * .9;
        Avalonia.Controls.Documents.TextElement.SetForeground(_border, palette.Token("common-black"));
        _popup.IsOpen = true;
    }
    private SheetPalette Palette => SheetPalette.For(_editor?.ActualThemeVariant ?? Avalonia.Styling.ThemeVariant.Light);
    private static TextBlock BodyText(string text) => new() { Text = text, FontSize = 12, LineHeight = 16.8, TextWrapping = TextWrapping.Wrap };
    private void RenderDetail(string name, int active, JsonElement info)
    {
        _content.Children.Clear(); _border.Width = 322; _border.Padding = new Thickness(0);
        var palette = Palette;
        var header = new Grid { ColumnDefinitions = new("*,20"), ColumnSpacing = 8, Margin = new Thickness(12) };
        var main = new StackPanel { Spacing = 4 };
        var signature = BodyText(""); signature.Foreground = palette.Token("grey-600");
        signature.Inlines = new InlineCollection { new Run(name.ToUpperInvariant()) { FontWeight = FontWeight.Bold, Foreground = palette.Token("common-black") }, new Run(" ( ") };
        var args = info.GetProperty("args").EnumerateArray().ToArray(); active = Math.Min(active, args.Length - 1);
        static string Display(string arg) => arg == "..." ? "…" : arg.EndsWith('*') ? "[" + arg[..^1] + "]" : arg;
        for (var i = 0; i < args.Length; i++)
        {
            if (i > 0) signature.Inlines.Add(new Run(", "));
            var argument = Display(args[i][0].GetString()!);
            if (i != active) signature.Inlines.Add(new Run(argument));
            else
            {
                var accent = ((ISolidColorBrush)palette.Accent).Color;
                signature.Inlines.Add(new InlineUIContainer { Child = new Border { Padding = new Thickness(3, 1), CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(Color.FromArgb(51, accent.R, accent.G, accent.B)), Child = new TextBlock { Text = argument, FontSize = 12, Foreground = palette.Token("common-black") } } });
            }
        }
        signature.Inlines.Add(new Run(" )")); main.Children.Add(signature);
        if (!_collapsed)
        {
            var description = BodyText(""); description.Foreground = palette.Token("grey-700");
            description.Inlines = new InlineCollection { new Run(info.GetProperty("description").GetString()) };
            if (DocsUrl(name, info.GetProperty("category").GetInt32()) is { } url)
            {
                var more = WorkbookView.Button("Read more", () => OpenLink?.Invoke(url)); more.Focusable = false;
                more.Height = 16.8; more.Padding = new Thickness(0); more.Margin = new Thickness(4, 0, 0, 0); more.Foreground = palette.Accent;
                description.Inlines.Add(new InlineUIContainer { Child = more });
            }
            main.Children.Add(description);
        }
        header.Children.Add(main);
        var collapse = WorkbookView.Button("", () => { _collapsed = !_collapsed; RenderDetail(name, active, info); }, _collapsed ? "Expand" : "Collapse");
        collapse.Content = new WorkbookIcon(_collapsed ? "ChevronDown" : "ChevronUp", 16);
        collapse.Width = collapse.Height = 24; collapse.Padding = new Thickness(4); collapse.Margin = new Thickness(0, -4, -4, 0);
        collapse.VerticalAlignment = VerticalAlignment.Top; collapse.Focusable = false;
        Grid.SetColumn(collapse, 1); header.Children.Add(collapse); _content.Children.Add(header);
        var examples = info.GetProperty("examples").EnumerateArray().ToArray();
        if (_collapsed || examples.Length == 0) return;
        var body = new StackPanel { Spacing = 4 };
        body.Children.Add(new TextBlock { Text = "Examples", FontSize = 11, Foreground = palette.Token("grey-600") });
        foreach (var example in examples) body.Children.Add(BodyText(example.GetString()!));
        _content.Children.Add(new Border { Child = body, Padding = new Thickness(12), BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = palette.Token("grey-200") });
    }
    internal static Uri? DocsUrl(string name, int category)
    {
        string[] folders = ["", "database", "date_and_time", "engineering", "financial", "information", "logical", "lookup_and_reference", "math_and_trigonometry", "statistical", "text"];
        if (category < 1 || category >= folders.Length) return null;
        var file = name.ToLowerInvariant(); var anchor = file.Replace('.', '-').Replace(' ', '-');
        return new Uri($"https://docs.ironcalc.com/functions/{folders[category]}/{file}.html" + (anchor == file ? "" : "#" + anchor));
    }
    private void RenderMatches()
    {
        _content.Children.Clear(); _border.Width = 330; _border.Padding = new Thickness(4);
        var list = new StackPanel(); var palette = Palette;
        var rows = new List<(Button Button, TextBlock Description)>();
        for (var i = 0; i < _matches.Length; i++)
        {
            var index = i; var name = _matches[i];
            var description = Functions[name.ToLowerInvariant()].GetProperty("description").GetString() ?? "";
            var sentence = Regex.Match(description, @"^.*?[.!?](\s|$)");
            var detail = BodyText((sentence.Success ? sentence.Value : description).Trim());
            detail.Foreground = palette.Token("grey-600"); detail.Margin = new Thickness(0, 4, 0, 0); detail.IsVisible = i == _selected;
            var label = BodyText(""); label.Foreground = palette.Token("common-black");
            label.Inlines = new InlineCollection { new Run(name[.._prefix.Length]) { TextDecorations = TextDecorations.Underline }, new Run(name[_prefix.Length..]) };
            var row = new StackPanel(); row.Children.Add(label); row.Children.Add(detail);
            var button = WorkbookView.Button("", () => { _selected = index; if (_editor is not null) Accept(_editor); });
            button.Content = row; button.Focusable = false; button.Height = double.NaN; button.Padding = new Thickness(8);
            button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.PointerEntered += (_, _) => { _selected = index; Highlight(); };
            rows.Add((button, detail)); list.Children.Add(button);
        }
        void Highlight()
        {
            for (var i = 0; i < rows.Count; i++)
            {
                rows[i].Description.IsVisible = i == _selected;
                rows[i].Button.Background = i == _selected ? palette.Token("grey-50") : Brushes.Transparent;
            }
        }
        Highlight();
        _content.Children.Add(new ScrollViewer { Content = list, MaxHeight = 320, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var selectedButton = rows[_selected].Button;
        Dispatcher.UIThread.Post(() => selectedButton.BringIntoView(), DispatcherPriority.Loaded);
    }
    public bool HandleKey(KeyEventArgs e, CellEditor editor)
    {
        if (!_popup.IsOpen) return false;
        if (e.Key == Key.Escape) { _dismissedEditors.Add(editor); Close(); return true; }
        if (_matches.Length == 0) return false;
        if (e.Key is Key.Up or Key.Down)
        {
            _selected = Math.Clamp(_selected + (e.Key == Key.Up ? -1 : 1), 0, _matches.Length - 1); RenderMatches(); return true;
        }
        if (e.Key is not (Key.Tab or Key.Enter)) return false;
        Accept(editor); return true;
    }
    private void Accept(CellEditor editor)
    {
        var text = editor.Text ?? "";
        var start = WorkbookView.Utf16Offset(text, _replaceFrom);
        var insert = _matches[_selected] + "(";
        editor.Text = text[..start] + insert + text[editor.CaretIndex..];
        editor.CaretIndex = start + insert.Length;
        Close(); editor.Focus();
    }
}
