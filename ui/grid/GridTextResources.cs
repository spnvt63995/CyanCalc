using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace CyanCalc.Tabular;

public readonly record struct GridFont(string Name = "Arial", double Size = 12, bool Bold = false,
    bool Italic = false, bool Underline = false, bool Strike = false, string? Color = null)
{
    public GridFont() : this("Arial", 12) { }
}

/// <summary>UI-thread text resources. No data source, workbook, or input state.</summary>
public sealed class GridTextResources : IDisposable
{
    const int MaxEntries = 1024, MaxTextUnits = 65_536, MaxEntryTextUnits = 4096;
    readonly Dictionary<TextKey, LinkedListNode<Entry>> entries = new();
    readonly LinkedList<Entry> recent = new();
    int textUnits;
    object? context;
    public int Count => entries.Count;
    public int RetainedTextUnits => textUnits;

    public void SetContext(object? value)
    {
        if (Equals(context, value)) return;
        Clear(); context = value;
    }

    public TextLease Text(string text, GridFont font, IBrush? foreground, bool wrap, double width)
    {
        if (string.IsNullOrWhiteSpace(font.Name) || !double.IsFinite(font.Size)) throw new ArgumentException("A finite font size and font family are required.");
        var key = new TextKey(text, font, foreground, wrap, wrap ? Math.Max(1, width) : double.PositiveInfinity);
        if (entries.TryGetValue(key, out var found))
        {
            recent.Remove(found); recent.AddFirst(found);
            return new(found.Value.Layout!, false);
        }
        var units = key.Text.Length + key.Font.Name.Length + (key.Font.Color?.Length ?? 0);
        var layout = Create(key);
        if (units > MaxEntryTextUnits) return new(layout, true);
        while (entries.Count >= MaxEntries || textUnits + units > MaxTextUnits)
        {
            var oldest = recent.Last!;
            entries.Remove(oldest.Value.Key); recent.RemoveLast();
            textUnits -= oldest.Value.TextUnits; oldest.Value.Dispose();
        }
        var node = recent.AddFirst(new Entry(key, layout, units));
        entries.Add(key, node); textUnits += units;
        return new(layout, false);
    }

    static TextLayout Create(TextKey key)
    {
        var font = key.Font;
        var typeface = new Typeface(new FontFamily(font.Name), font.Italic ? FontStyle.Italic : FontStyle.Normal,
            font.Bold ? FontWeight.Bold : FontWeight.Normal);
        var decorations = new TextDecorationCollection();
        if (font.Underline) decorations.Add(TextDecorations.Underline[0]);
        if (font.Strike) decorations.Add(TextDecorations.Strikethrough[0]);
        return new TextLayout(key.Text, typeface, Math.Max(1, font.Size),
            key.Foreground ?? new SolidColorBrush(Color.TryParse(font.Color, out var color) ? color : Color.Parse("#202838")),
            textAlignment: TextAlignment.Left, textWrapping: key.Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            textDecorations: decorations, maxWidth: key.Width);
    }

    public void Clear()
    {
        foreach (var entry in recent) entry.Dispose();
        entries.Clear(); recent.Clear(); textUnits = 0; context = null;
    }
    public void Dispose() => Clear();
    readonly record struct TextKey(string Text, GridFont Font, IBrush? Foreground, bool Wrap, double Width);
    sealed class Entry(TextKey key, TextLayout layout, int units) : IDisposable
    {
        public TextKey Key { get; } = key;
        public TextLayout? Layout { get; private set; } = layout;
        public int TextUnits { get; } = units;
        public void Dispose() { Layout?.Dispose(); Layout = null; }
    }
    public readonly struct TextLease(TextLayout layout, bool ownsLayout) : IDisposable
    {
        public TextLayout Layout { get; } = layout;
        public void Dispose() { if (ownsLayout) Layout.Dispose(); }
    }
}

public static class GridCellPainter
{
    public static void Text(DrawingContext context, TextLayout text, Rect area, Rect clip,
        string horizontal = "left", string vertical = "bottom")
    {
        if (area.Width <= 0 || clip.Width <= 1 || clip.Height <= 1) return;
        var x = horizontal switch { "right" => area.Right - text.Width, "center" or "centercontinuous" => area.X + (area.Width - text.Width) / 2, _ => area.X };
        var y = vertical switch { "top" => area.Y, "center" => area.Y + (area.Height - text.Height) / 2, _ => area.Bottom - text.Height };
        using var clipping = context.PushClip(clip.Deflate(.5));
        text.Draw(context, new Point(x, y));
    }

    public static void Header(DrawingContext context, TextLayout text, Rect rect)
    {
        using var clip = context.PushClip(rect);
        text.Draw(context, new Point(rect.X + (rect.Width - text.Width) / 2, rect.Y + (rect.Height - text.Height) / 2));
    }

    public static void ActiveCell(DrawingContext context, Rect rect, IBrush accent)
    {
        if (rect.Width <= 2 || rect.Height <= 2) return;
        context.DrawRectangle(null, new Pen(accent, 2), rect.Deflate(1), 3, 3);
        if (rect.Width > 6 && rect.Height > 6)
            context.DrawRectangle(null, new Pen(Brushes.White, 1), rect.Deflate(2.5), 1, 1);
    }
}
