using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;

namespace CyanCalc.Controls;

public sealed partial class WorkbookShell
{
    public static IReadOnlyList<(string Id, string Category)> Templates { get; } = new[]
    {
        ("yearly_calendar", "lifestyle"), ("crossword", "games"), ("travel_expenses_tracker", "lifestyle"),
        ("invoice", "finance"), ("gantt_project_tracker", "project_management"), ("weekly_timesheet", "project_management"),
        ("wordle", "games"), ("event_calendar", "lifestyle")
    };
    private Window Dialog(LocalizedText title, double width, double height) => title.Bind(new Window()
    {
        Width = width, Height = height, CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        RequestedThemeVariant = SelectedTheme
    }, Window.TitleProperty);
    public async Task<bool> ConfirmDeleteAsync(Window owner, IReadOnlyList<WorkbookListEntry> entries)
    {
        if (entries.Count == 0) return false;
        var dialog = new Window(); NativeDialogTheme.Follow(dialog, this);
        var content = WorkbookDialog.Body();
        var message = entries.Count == 1
            ? Text.Key("file_bar.file_menu.delete_workbook.subtitle", ("workbookName", entries[0].Name))
            : Text.Key("native.delete_many", ("count", entries.Count));
        content.Children.Add(WorkbookDialog.Paragraph(message));
        // Local libraries have no shared server links. Keep the corresponding native wording.
        var warning = WorkbookDialog.Paragraph(Text.Key("native.delete_warning")); warning.FontWeight = FontWeight.Bold;
        content.Children.Add(warning);
        var delete = WorkbookDialog.Button(Text.Key("file_bar.file_menu.delete_workbook.confirm_button"), () => dialog.Close(true), "destructive");
        var actions = WorkbookDialog.Footer(WorkbookDialog.Button(Text.Key("file_bar.file_menu.delete_workbook.cancel_button"), () => dialog.Close(false)), delete);
        WorkbookDialog.Configure(dialog, Text.Key("file_bar.file_menu.delete_workbook.title"), Text.Key("link_dialog.close"), content, actions);
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { dialog.Close(false); e.Handled = true; } };
        return await dialog.ShowDialog<bool>(owner);
    }
    public async Task<string?> ChooseTemplateAsync(Window owner, bool welcome = false)
    {
        var title = welcome ? Text.Compose(t => t["welcome_dialog.title"].Replace("IronCalc", "CyanCalc")) : Text.Key("welcome_dialog.templates.choose_template");
        var dialog = new Window(); NativeDialogTheme.Follow(dialog, this);
        WorkbookDialog.Configure(dialog, title, Text.Key("welcome_dialog.close_dialog"), new Border(), null, 780);
        WorkbookChrome.Attach(dialog);
        dialog.SizeToContent = SizeToContent.Manual;
        dialog.Width = Math.Min(782, Math.Max(240, owner.ClientSize.Width - 32));
        dialog.Height = Math.Min(welcome ? 462 : 562, Math.Max(240, owner.ClientSize.Height - 32));
        var bitmaps = new List<Bitmap>();
        var root = new Grid { RowDefinitions = new("44,Auto,*") };
        var heading = new Grid { ColumnDefinitions = new("*,28"), Margin = new Thickness(12, 0) };
        heading.Children.Add(title.Bind(new TextBlock { FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }, TextBlock.TextProperty));
        var close = IconButton("X", Text.Key("welcome_dialog.close_dialog"), () => dialog.Close());
        close.Name = "DialogClose"; close.Width = close.Height = 28;
        Grid.SetColumn(close, 1); heading.Children.Add(close); root.Children.Add(heading);
        var filters = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 12, 8) };
        var categories = new Dictionary<string, Button>();
        var cards = new global::Avalonia.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(2, 8, 2, 0), VerticalAlignment = VerticalAlignment.Top };
        var scroll = new ScrollViewer { Content = cards, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(filters, 1); root.Children.Add(filters); Grid.SetRow(scroll, 2); root.Children.Add(scroll);
        void Populate(string category)
        {
            foreach (var (key, filter) in categories) filter.Classes.Set("selected", key == category);
            cards.Children.Clear();
            foreach (var bitmap in bitmaps) bitmap.Dispose(); bitmaps.Clear();
            foreach (var (id, group) in Templates.Where(t => category == "all" || t.Category == category))
            {
                using var stream = AssetLoader.Open(new Uri($"avares://CyanCalc.Avalonia/Shell/Templates/{id}.png"));
                var bitmap = new Bitmap(stream); bitmaps.Add(bitmap);
                var thumbnail = new Border { Name = "TemplateThumbnail", ClipToBounds = true, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = ConditionalRenderer.Brush("#10000000"),
                    Child = new Image { Source = bitmap, Stretch = Stretch.UniformToFill } };
                thumbnail.SizeChanged += (_, _) => thumbnail.Height = thumbnail.Bounds.Width * 132 / 200;
                var text = new StackPanel { Spacing = 4, Margin = new Thickness(6, 0) };
                text.Children.Add(Text.Key("welcome_dialog.templates." + id).Bind(new TextBlock { FontWeight = FontWeight.SemiBold }, TextBlock.TextProperty));
                var categoryText = Text.Key("welcome_dialog.templates.category_" + group).Block();
                categoryText.Bind(TextBlock.ForegroundProperty, categoryText.GetResourceObservable("Dialog-grey-500")); text.Children.Add(categoryText);
                var content = new StackPanel { Spacing = 12, Children = { thumbnail, text } };
                var button = TextButton("", () => dialog.Close(id)); button.Classes.Add("template-card");
                button.Content = content; button.Padding = new Thickness(0); button.Height = double.NaN;
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.VerticalContentAlignment = VerticalAlignment.Top;
                button.Margin = new Thickness(10, 0, 10, 20); Text.Key("welcome_dialog.templates." + id).Describe(button);
                cards.Children.Add(button);
            }
        }
        if (!welcome)
            foreach (var category in new[] { "all", "lifestyle", "games", "finance", "project_management" })
            {
                var button = TextButton(Text.Key("welcome_dialog.templates.category_" + category), () => Populate(category));
                button.Classes.Add("template-filter"); button.Padding = new Thickness(10, 4); button.Height = double.NaN;
                button.CornerRadius = new CornerRadius(99); button.Margin = new Thickness(0, 0, 4, 0);
                categories[category] = button; filters.Children.Add(button);
            }
        Control body = root;
        if (welcome)
        {
            root.Children.Remove(heading); root.Children.Remove(filters); Grid.SetRow(scroll, 1);
            root.RowDefinitions = new RowDefinitions("52,*");
            var templateHeading = new Grid { ColumnDefinitions = new("*,28"), Margin = new Thickness(20, 0) };
            templateHeading.Children.Add(Text.Key("welcome_dialog.templates.examples_and_templates").Bind(new TextBlock { FontSize = 14, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }, TextBlock.TextProperty));
            heading.Children.Remove(close); Grid.SetColumn(close, 1); templateHeading.Children.Add(close); root.Children.Add(templateHeading);
            cards.Margin = new Thickness(10, 8, 10, 0);
            var actions = new StackPanel { Spacing = 24, Margin = new Thickness(16) };
            var logo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var icon = new Border { Padding = new Thickness(6), CornerRadius = new CornerRadius(6), Child = new WorkbookIcon("Table2", 18) };
            icon.Bind(Border.BackgroundProperty, icon.GetResourceObservable("Dialog-primary-main"));
            logo.Children.Add(icon); logo.Children.Add(new TextBlock { Text = "CyanCalc", FontSize = 14, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }); actions.Children.Add(logo);
            var buttons = new StackPanel { Spacing = 4, Margin = new Thickness(-8, 0) };
            foreach (var (key, glyph, value) in new[] { ("blank_workbook", "Plus", "blank"), ("import_workbook", "FileUp", "import") })
            {
                var button = WorkbookDialog.Button(Text.Key("welcome_dialog." + key), () => dialog.Close(value), "ghost", glyph);
                button.HorizontalContentAlignment = HorizontalAlignment.Left; buttons.Children.Add(button);
            }
            actions.Children.Add(buttons);
            var left = new Grid { RowDefinitions = new("Auto,*,Auto") }; left.Children.Add(actions);
            var warning = new Border { Padding = new Thickness(8), Margin = new Thickness(12), CornerRadius = new CornerRadius(6), Child = WorkbookDialog.Paragraph(Text.Key("native.storage_help")) };
            warning.Bind(Border.BackgroundProperty, warning.GetResourceObservable("Dialog-grey-200")); Grid.SetRow(warning, 2); left.Children.Add(warning);
            var leftFrame = new Border { Child = left, BorderThickness = new Thickness(0, 0, 1, 0) };
            leftFrame.Bind(Border.BackgroundProperty, leftFrame.GetResourceObservable("Dialog-grey-50"));
            leftFrame.Bind(Border.BorderBrushProperty, leftFrame.GetResourceObservable("Dialog-grey-200"));
            var columns = new Grid { ColumnDefinitions = new("207,*") }; columns.Children.Add(leftFrame); Grid.SetColumn(root, 1); columns.Children.Add(root); body = columns;
        }
        else scroll.SizeChanged += (_, _) => cards.Columns = dialog.Width < 768 ? 2 : 3;
        var frame = new Border { Name = "DialogFrame", Child = body, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), ClipToBounds = true };
        frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable("Dialog-common-white")); frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("Dialog-grey-300"));
        Populate("all"); dialog.Content = frame;
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { dialog.Close(); e.Handled = true; } };
        try { return await dialog.ShowDialog<string?>(owner); }
        finally { foreach (var bitmap in bitmaps) bitmap.Dispose(); }
    }
    public async Task ImportAsync(Window owner, Func<IStorageFile, Task> import)
    {
        var dialog = new Window(); NativeDialogTheme.Follow(dialog, this);
        var zone = new Grid { Height = 160, Margin = new Thickness(12, 4) };
        var outline = new Avalonia.Controls.Shapes.Rectangle { RadiusX = 6, RadiusY = 6, StrokeThickness = 1,
            StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 3, 3 }, IsHitTestVisible = false };
        zone.Children.Add(outline);
        var prompt = new StackPanel { Spacing = 16, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var icon = new WorkbookIcon("FileUp");
        var tile = new Border { Padding = new Thickness(6), CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Center, Child = icon };
        prompt.Children.Add(tile);
        // The upstream drop-zone button retains the browser's Arial default.
        var instruction = new TextBlock { FontFamily = new FontFamily("Arial"), FontWeight = FontWeight.Normal,
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
        var subtitle = Text.Key("file_bar.file_menu.import.subtitle").Bind(new Run(), Run.TextProperty);
        var browseText = Text.Key("file_bar.file_menu.import.subtitle_link").Bind(new Run(), Run.TextProperty);
        instruction.Inlines!.Add(subtitle); instruction.Inlines.Add(new Run(" ")); instruction.Inlines.Add(browseText);
        prompt.Children.Add(instruction);
        var dropText = WorkbookDialog.Paragraph(Text.Key("file_bar.file_menu.import.drop_file_here"));
        dropText.FontFamily = instruction.FontFamily; dropText.FontWeight = FontWeight.Normal;
        dropText.IsVisible = false; dropText.TextAlignment = TextAlignment.Center; prompt.Children.Add(dropText);
        var message = new TextBlock { Name = "ImportMessage", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(12), IsVisible = false };
        var browse = new WorkbookButton();
        WorkbookDialog.StyleButton(browse, Text.Key("file_bar.file_menu.import.subtitle_link"), "ghost");
        browse.Click += async (_, _) =>
        {
            var files = await dialog.StorageProvider.OpenFilePickerAsync(new() { Title = Text["file_bar.file_menu.import.title"], AllowMultiple = true });
            await Task.WhenAll(files.Select(Upload));
        };
        browse.Name = "ImportBrowse"; browse.Content = prompt; browse.Height = 160;
        browse.HorizontalAlignment = HorizontalAlignment.Stretch; browse.Padding = new Thickness(12);
        browse.Background = Brushes.Transparent;
        zone.Children.Add(browse); zone.Children.Add(message);
        bool dragging = false;
        void Paint()
        {
            var palette = SheetPalette.For(dialog.ActualThemeVariant);
            var accent = ((ISolidColorBrush)palette.Token("primary-main")).Color;
            var hover = browse.IsPointerOver || dragging;
            outline.Stroke = palette.Token(hover ? "primary-main" : "primary-light");
            outline.Fill = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops { new(Color.FromArgb(hover ? (byte)31 : (byte)20, accent.R, accent.G, accent.B), 0), new(Colors.Transparent, 1) } };
            icon.Foreground = palette.Token("primary-light"); tile.Background = new SolidColorBrush(Color.FromArgb(26, accent.R, accent.G, accent.B));
            instruction.Foreground = palette.Token("grey-900"); browseText.Foreground = palette.Token("primary-main");
            instruction.IsVisible = !dragging; dropText.IsVisible = dragging;
            prompt.Spacing = hover ? 8 : 16;
        }
        browse.PointerEntered += (_, _) => Paint(); browse.PointerExited += (_, _) => Paint();
        dialog.ActualThemeVariantChanged += (_, _) => Paint(); Paint();
        async Task Upload(IStorageFile file)
        {
            browse.IsVisible = false; message.IsVisible = true;
            Text.Key("file_bar.file_menu.import.uploading", ("fileName", file.Name)).Bind(message, TextBlock.TextProperty);
            try { using (file) await import(file); if (dialog.IsVisible) dialog.Close(); }
            catch (Exception e) { message.Foreground = SheetPalette.For(dialog.ActualThemeVariant).Token("error-main"); Text.Compose(_ => "Error: " + e.Message).Bind(message, TextBlock.TextProperty); }
        }
        var learn = WorkbookDialog.Button(Text.Key("file_bar.file_menu.import.learn_more"), () =>
            Send("link", value: "https://docs.ironcalc.com/web-application/importing-files.html"), "ghost", "BookOpen", 16);
        learn.Height = 44; learn.Padding = new Thickness(0); learn.HorizontalAlignment = HorizontalAlignment.Left;
        learn.FontWeight = FontWeight.Normal;
        learn.Bind(Button.ForegroundProperty, learn.GetResourceObservable("Dialog-grey-600"));
        var footer = new Border { Margin = new Thickness(12, 0), Child = learn };
        WorkbookDialog.Configure(dialog, Text.Key("file_bar.file_menu.import.title"), Text.Key("file_bar.file_menu.import.close_dialog"), zone, footer, 460, import: true);
        DragDrop.SetAllowDrop(zone, true);
        zone.AddHandler(DragDrop.DragOverEvent, (_, e) => { dragging = true; Paint(); e.DragEffects = DragDropEffects.Copy; e.Handled = true; });
        zone.AddHandler(DragDrop.DragLeaveEvent, (_, _) => { dragging = false; Paint(); });
        zone.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            e.Handled = true; dragging = false; Paint();
            // Upstream accepts all picked files, but only the first dropped file.
            if (e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().FirstOrDefault() is { } file) await Upload(file);
        });
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { dialog.Close(); e.Handled = true; } };
        await dialog.ShowDialog(owner);
    }
}
