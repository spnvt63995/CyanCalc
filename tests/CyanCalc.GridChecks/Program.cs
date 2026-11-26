using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using CyanCalc.Tabular;

internal static class Program
{
    static int checks;
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            AppBuilder.Configure<TestApp>().UseHeadless(new() { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
            Axis(); Text(); Surface(args.FirstOrDefault());
            Console.WriteLine($"GRID_CHECKS_PASS {checks} assertions (no workbook engine)"); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    static void Check(bool condition, string description)
    { if (!condition) throw new InvalidOperationException(description); checks++; }
    static void Axis()
    {
        var axis = GridAxis.Build(1, 4, 20, 30, 130, ids => ids.Select(i => i == 4 ? 0d : 25d).ToArray());
        Check(axis.Select(c => c.Index).SequenceEqual([1L, 5L, 6L, 7L]), "frozen, skipped and hidden rows");
        Check(axis[1].Start == 58 && GridAxis.HitTest(axis, 58) == 5, "frozen separator and exact hit boundary");
        var large = GridAxis.Build(0, 3_000_000_000, 4_000_000_000, 30, 100, ids => ids.Select(_ => 25d).ToArray());
        Check(large.Length == 3 && large[0].Index == 3_000_000_000, "positions beyond Int32/worksheet limits");
        var shifted = GridAxis.Build(1, 2, 5, 30, 150, ids => ids.Select(_ => 25d).ToArray(), 10);
        var rect = GridAxis.Rectangles(axis, shifted, 5, 6, 2, 3).Single();
        Check(rect.X == 58, "selection clipped at frozen boundary");
        try { GridAxis.Build(0, 1, 10, 0, 100, _ => []); throw new Exception("missing dimension guard"); }
        catch (ArgumentException) { checks++; }
    }
    static void Text()
    {
        using var cache = new GridTextResources(); var font = new GridFont();
        var first = cache.Text("같은 값", font, Brushes.Black, false, 80).Layout;
        Check(ReferenceEquals(first, cache.Text("같은 값", font, Brushes.Black, false, 120).Layout), "unwrapped layout reused across column resize");
        Check(!ReferenceEquals(first, cache.Text("같은 값", font, Brushes.Black, true, 80).Layout), "wrapped text uses a different layout");
        for (var n = 0; n < 2500; n++) using (cache.Text("row " + n, font, Brushes.Black, false, 80)) { }
        Check(cache.Count <= 1024 && cache.RetainedTextUnits <= 65_536, "long scroll has bounded text cache");
        var count = cache.Count;
        using (cache.Text(new string('x', 5000), font, Brushes.Black, true, 100)) { }
        Check(cache.Count == count, "long value is not retained");
        cache.SetContext("another-result"); Check(cache.Count == 0, "result switch releases layouts");
    }
    static void Surface(string? output)
    {
        Check(!typeof(ReadOnlyTable).Assembly.GetReferencedAssemblies().Any(a => a.Name is "CyanCalc" or "CyanCalc.Avalonia"), "grid has no workbook references");
        var reads = 0;
        var table = new ReadOnlyTable { FrozenColumns = 1 };
        table.Snapshot = new("one", [new("id", "DB key", Width: 110), new("text", "한글 값", Width: 250), new("null", "Nullable", Width: 150)],
            1_000_000, (r, c) => { reads++; return c switch { 0 => new((r + 1).ToString(), TableCellKind.Number), 1 => new("=한글\t원문"), _ => new("", TableCellKind.Null) }; });
        var window = new Window { Width = 700, Height = 420, Content = table }; window.Show(); Pump(window);
        try
        {
            reads = 0; var light = Render(table);
            Check(reads > 0 && reads < 100, "draw requests visible cells only");
            table.Select(new(0, 0)); table.Select(new(1, 2), true);
            Check(table.SelectionText() == "1\t\"=한글\t원문\"\tNULL" + Environment.NewLine + "2\t\"=한글\t원문\"\tNULL", "copy keeps formula-like text, tabs, null and row boundaries");
            string copy = table.SelectionText();
            var beforeLinks = Render(table);
            table.HighlightCells([new(0, 2), new(3, 0)]); Pump(window);
            Check(!beforeLinks.SequenceEqual(Render(table)) && table.SelectionText() == copy, "disjoint highlights render without changing the copy range");
            table.ClearSelection(); Pump(window);
            Check(table.SelectionText() == "" && !light.SequenceEqual(Render(table)), "highlights can exist without a rectangular selection");
            table.Snapshot = table.Snapshot! with { Identity = "same-shape-new-result" }; Pump(window);
            Check(light.SequenceEqual(Render(table)), "new result clears disjoint highlights");
            table.Select(new(0, 0));
            window.MouseDown(new(100, 45), MouseButton.Left); window.MouseUp(new(100, 45), MouseButton.Left);
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None); Pump(window);
            Check(table.Selection?.Active.Row == 1, "keyboard moves selection");
            table.Snapshot = new("large", [new("id", "Position")], long.MaxValue, (r, _) => new(r.ToString()));
            table.Select(new(long.MaxValue - 2, 0));
            window.KeyPressQwerty(PhysicalKey.PageDown, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.PageDown, RawInputModifiers.None); Pump(window);
            Check(table.Selection?.Active.Row == long.MaxValue - 1, "page movement saturates at Int64 row limit");
            window.MouseDown(new(100, 45), MouseButton.Left);
            table.Snapshot = new("two", [new("a", "Replacement")], 1, (_, _) => new("new") { DisplayText = "Compact" });
            window.MouseMove(new(300, 90), RawInputModifiers.LeftMouseButton); window.MouseUp(new(300, 90), MouseButton.Left); Pump(window);
            Check(table.Selection is null, "replacing a result cancels a captured drag");
            table.Select(new(0, 0)); Check(table.SelectionText() == "new", "copy reads replacement result only");
            window.RequestedThemeVariant = ThemeVariant.Dark; Pump(window); var dark = Render(table);
            Check(!light.SequenceEqual(dark), "theme change produces a new frame");
            table.Snapshot = new("loading", [new("a", "Pending")], 1, (_, _) => new("Loading…", TableCellKind.Loading), CountIsExact: false);
            table.Select(new(0, 0));
            try { table.SelectionText(); throw new Exception("missing loading guard"); }
            catch (InvalidOperationException) { checks++; }
            table.Snapshot = new("empty", [new("a", "Empty result")], 0, (_, _) => throw new Exception("must not access an empty result"));
            Pump(window); Render(table); Check(table.Selection is null, "empty result clears selection without reading cells");
            if (output is not null) { Directory.CreateDirectory(output); File.WriteAllBytes(Path.Combine(output, "light.png"), light); File.WriteAllBytes(Path.Combine(output, "dark.png"), dark); }
        }
        finally { window.Content = null; window.Close(); Pump(window); }
    }
    static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    static byte[] Render(Control control)
    {
        using var bitmap = new RenderTargetBitmap(new((int)control.Bounds.Width, (int)control.Bounds.Height), new(96, 96));
        bitmap.Render(control); using var stream = new MemoryStream(); bitmap.Save(stream); return stream.ToArray();
    }
    sealed class TestApp : Application { public override void Initialize() => Styles.Add(new FluentTheme()); }
}
