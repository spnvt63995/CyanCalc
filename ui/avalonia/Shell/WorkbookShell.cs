using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace CyanCalc.Controls;

public sealed record WorkbookListEntry(Guid Id, string Name, DateTimeOffset CreatedAt, bool Pinned);
public sealed record WorkbookShellRequest(string Action, Guid? Workbook = null, string? Value = null, Guid[]? Workbooks = null);

/// <summary>Optional application chrome around WorkbookView, matching the upstream
/// FileBar/LeftDrawer. The host handles persistence, dialogs, imports and navigation.</summary>
public sealed partial class WorkbookShell : UserControl
{
    public WorkbookView Workbook { get; } = new();
    public WorkbookText Text => Workbook.Text;
    public event EventHandler<WorkbookShellRequest>? Requested;
    private readonly Grid _layout = new() { ColumnDefinitions = new("0,*"), ClipToBounds = true };
    private readonly ThemeVariantScope _themeScope = new();
    public ThemeVariant SelectedTheme => _themeScope.RequestedThemeVariant ?? ThemeVariant.Default;
    private readonly Grid _main = new() { RowDefinitions = new("52,Auto,*") };
    private readonly Border _drawer = new() { Width = 264, BorderThickness = new Thickness(0, 0, 1, 0), IsVisible = false };
    private readonly Grid _drawerLayout = new() { RowDefinitions = new("52,*,Auto,Auto") };
    private readonly StackPanel _list = new() { Spacing = 2, Margin = new Thickness(12, 16) };
    private readonly Border _drawerHeader = new() { BorderThickness = new Thickness(0, 0, 0, 1) };
    private readonly StackPanel _footer = new() { Margin = new Thickness(12, 8), Spacing = 4 };
    private readonly Border _notice = new() { Margin = new Thickness(12), Padding = new Thickness(10), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
    private readonly Border _fileBar = new() { BorderThickness = new Thickness(0, 0, 0, 1) };
    private readonly TextBox _title = new() { Classes = { "workbook-title" }, MinWidth = 32, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeight.SemiBold, BorderThickness = new Thickness(0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, FontSize = 14 };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 5) };
    private IDisposable? _messageTranslation;
    private readonly Border _messageBar = new() { IsVisible = false };
    private readonly Border _overlay = new() { Cursor = new Cursor(StandardCursorType.Hand), IsVisible = false };
    private TopLevel? _viewport;
    private WorkbookListEntry[] _entries = [];
    private readonly Dictionary<Guid, Border> _rows = [];
    private readonly HashSet<Guid> _checked = [];
    private Guid? _lastChecked;
    private Guid? _selected;
    private string _name = "", _query = "", _appearance = "system";
    private bool _searching, _noticeDismissed, _drawerOpen;
    private MenuFlyout? _menu;
    private Button? _fileButton, _helpButton;
    public bool IsDrawerOpen => _drawerOpen;

    public WorkbookShell()
    {
        WorkbookChrome.Attach(this); FontFamily = WorkbookDialog.Font; FontSize = 12;
        _title.TextChanged += (_, _) =>
        {
            using var text = new Avalonia.Media.TextFormatting.TextLayout(_title.Text ?? "", new Typeface(WorkbookDialog.Font, FontStyle.Normal, FontWeight.SemiBold), 14, Brushes.Black);
            _title.Width = Math.Max(32, Math.Ceiling(text.WidthIncludingTrailingWhitespace) + 18);
        };
        _drawerLayout.Children.Add(_drawerHeader);
        var scroll = new ScrollViewer { Content = _list, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); _drawerLayout.Children.Add(scroll);
        Grid.SetRow(_notice, 2); _drawerLayout.Children.Add(_notice);
        Grid.SetRow(_footer, 3); _drawerLayout.Children.Add(_footer);
        _drawer.Child = _drawerLayout;
        _main.Children.Add(_fileBar);
        _messageBar.Child = _message; Grid.SetRow(_messageBar, 1); _main.Children.Add(_messageBar);
        Grid.SetRow(Workbook, 2); _main.Children.Add(Workbook);
        Grid.SetRowSpan(_overlay, 3); _main.Children.Add(_overlay);
        _overlay.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(_overlay).Properties.IsLeftButtonPressed) return;
            e.Pointer.Capture(_overlay); e.Handled = true;
        };
        _overlay.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left || e.Pointer.Captured != _overlay) return;
            var inside = _layout.InputHitTest(e.GetPosition(_layout)) == _overlay;
            e.Pointer.Capture(null);
            if (inside) ToggleDrawer();
            e.Handled = true;
        };
        _layout.Children.Add(_drawer); Grid.SetColumn(_main, 1); _layout.Children.Add(_main); _themeScope.Child = _layout; Content = _themeScope;
        Text.Key("native.workbook_title").Describe(_title);
        _title.LostFocus += (_, _) => CommitTitle();
        _title.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { CommitTitle(); Workbook.FocusCell(); e.Handled = true; }
            else if (e.Key == Key.Escape) { _title.Text = _name; e.Handled = true; }
        };
        SizeChanged += (_, _) => { LayoutDrawer(); BuildFileBar(); };
        AttachedToVisualTree += (_, _) =>
        {
            _viewport = TopLevel.GetTopLevel(this);
            if (_viewport is not null) _viewport.PropertyChanged += ViewportChanged;
            LayoutDrawer();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (_viewport is not null) _viewport.PropertyChanged -= ViewportChanged;
            _viewport = null;
        };
        _themeScope.ActualThemeVariantChanged += (_, _) => ApplyPalette();
        RefreshChrome();
    }
    public void SetLibrary(IEnumerable<WorkbookListEntry> entries, Guid? selected, string name)
    {
        var next = entries.ToArray();
        var changed = !_entries.SequenceEqual(next);
        _entries = next; _selected = selected; _name = name;
        if (!_title.IsFocused) _title.Text = name;
        _checked.RemoveWhere(id => !_entries.Any(e => e.Id == id));
        if (changed) BuildList(); else RefreshRowColors();
    }
    public void SetPreferences(string language, string appearance, bool noticeDismissed)
    {
        Text.Language = language; _appearance = appearance; _noticeDismissed = noticeDismissed;
        _themeScope.RequestedThemeVariant = appearance switch { "dark" => ThemeVariant.Dark, "light" => ThemeVariant.Light, _ => ThemeVariant.Default };
        RefreshChrome();
    }
    public void ShowMessage(string? message)
    {
        _messageTranslation?.Dispose(); _messageTranslation = null;
        _message.Text = message; _messageBar.IsVisible = !string.IsNullOrEmpty(message);
    }
    public void ShowLocalizedMessage(LocalizedText message)
    {
        _messageTranslation?.Dispose();
        _messageTranslation = message.BindTo(_message, TextBlock.TextProperty); _messageBar.IsVisible = true;
    }
    public void ClearChecked() { _checked.Clear(); _lastChecked = null; BuildList(); }
    private void CommitTitle()
    {
        if (_title.Text is { } name && name != _name) Send("rename", value: name);
    }
    private void Send(string action, Guid? id = null, string? value = null, Guid[]? ids = null) =>
        Requested?.Invoke(this, new(action, id, value, ids));
    private void ToggleDrawer()
    {
        _drawerOpen = !_drawerOpen; LayoutDrawer(); BuildFileBar();
    }
    private void LayoutDrawer()
    {
        // App.css uses max-width:768px on the viewport and keeps the open main
        // content 768px wide, clipped by the wrapper beside the 264px sidebar.
        var compact = _drawerOpen && (_viewport?.ClientSize.Width ?? Bounds.Width) is > 0 and <= 768;
        _layout.ColumnDefinitions[0].Width = new GridLength(_drawerOpen ? 264 : 0);
        _drawer.IsVisible = _drawerOpen;
        _main.MinWidth = compact ? 768 : 0;
        _main.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        _overlay.IsVisible = compact;
    }
    private void ViewportChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TopLevel.ClientSizeProperty) LayoutDrawer();
    }
    private void RefreshChrome() { BuildHeader(); BuildList(); BuildFileBar(); BuildNotice(); ApplyPalette(); }
    private SheetPalette Palette => SheetPalette.For(_themeScope.ActualThemeVariant);
    private IBrush BackgroundBrush => Palette.Chrome;
    private IBrush DrawerBrush => Palette.Chrome;
    private IBrush SeparatorBrush => Palette.Token("grey-300");
    private void ApplyPalette()
    {
        Foreground = SheetPalette.For(_themeScope.ActualThemeVariant).DefaultText;
        _drawer.Background = DrawerBrush;
        _drawer.BorderBrush = _drawerHeader.BorderBrush = _fileBar.BorderBrush = _notice.BorderBrush = SeparatorBrush;
        _fileBar.Background = _notice.Background = BackgroundBrush;
        _overlay.Background = Palette.Token("shell-overlay");
        _messageBar.Background = DrawerBrush;
        RefreshRowColors();
    }
    private void RefreshRowColors()
    {
        foreach (var (id, row) in _rows) row.Background = id == _selected ? Palette.HeaderSelected : Brushes.Transparent;
    }
    internal static Button IconButton(string icon, string label, Action action)
    {
        var button = new WorkbookButton { Classes = { "workbook-flat" }, Content = new WorkbookIcon(icon), Width = 30, Height = 30, MinHeight = 0, Padding = new Thickness(6), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        ToolTip.SetTip(button, label); AutomationProperties.SetName(button, label); button.Click += (_, _) => action(); return button;
    }
    internal static Button IconButton(string icon, LocalizedText label, Action action) => label.Describe(IconButton(icon, "", action));
    internal static Button TextButton(LocalizedText label, Action action) => label.Describe(label.Bind(TextButton("", action), ContentControl.ContentProperty));
    internal static Button TextButton(string label, Action action)
    {
        var button = new WorkbookButton { Classes = { "workbook-flat" }, Content = label, Padding = new Thickness(12, 0), HorizontalContentAlignment = HorizontalAlignment.Center };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action(); return button;
    }
}
