using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace CyanCalc.Controls;

// UI-thread resources owned by one canvas. Keys contain rendered values, never model objects.
// Bound both entry count and retained text; exceptionally long values use a disposable one-shot layout.
internal sealed class WorksheetRenderResources
{
    private const int MaxEntries = 1024;
    private const int MaxTextUnits = 65_536;
    private const int MaxEntryTextUnits = 4096;
    private static readonly CellFont HeaderFont = new() { Name = "Inter", B = true, Sz = 12 };
    private readonly Dictionary<TextKey, LinkedListNode<Entry>> _entries = new();
    private readonly LinkedList<Entry> _recent = new();
    private int _textUnits;
    private (Guid Session, int Sheet, SheetPalette Palette, double Scale)? _context;
    private Pen? _gridPen;

    internal int Count => _entries.Count;
    internal int RetainedTextUnits => _textUnits;

    internal void SetContext(Guid? session, int sheet, SheetPalette palette, double scale)
    {
        var next = session is { } id ? ((Guid, int, SheetPalette, double)?)(id, sheet, palette, scale) : null;
        if (_context == next) return;
        Clear();
        _context = next;
    }

    internal Pen GridPen(SheetPalette palette) => _gridPen ??= new Pen(palette.Grid, 1);

    internal TextLease CellText(string text, CellStyle style, SheetPalette palette, bool wrap, double width) =>
        Get(new(text, style.Font, style.Font.Color is not null ? null :
            style.Fill.Color is not null ? palette.FilledCellText : palette.DefaultText,
            wrap, wrap ? Math.Max(1, width) : double.PositiveInfinity));

    internal TextLease HeaderText(string text, IBrush foreground) =>
        Get(new(text, HeaderFont, foreground, false, double.PositiveInfinity));

    private TextLease Get(TextKey key)
    {
        if (_entries.TryGetValue(key, out var node))
        {
            _recent.Remove(node);
            _recent.AddFirst(node);
            return new(node.Value.Layout!, false);
        }
        var units = key.Text.Length + key.Font.Name.Length + (key.Font.Color?.Length ?? 0);
        var layout = Create(key);
        if (units > MaxEntryTextUnits) return new(layout, true);
        while (_entries.Count >= MaxEntries || _textUnits + units > MaxTextUnits)
        {
            var oldest = _recent.Last!;
            _entries.Remove(oldest.Value.Key);
            _recent.RemoveLast();
            _textUnits -= oldest.Value.TextUnits;
            oldest.Value.Dispose();
        }
        node = _recent.AddFirst(new Entry(key, layout, units));
        _entries.Add(key, node);
        _textUnits += units;
        return new(layout, false);
    }

    private static TextLayout Create(TextKey key)
    {
        var font = key.Font;
        var typeface = new Typeface(new FontFamily(font.Name), font.I ? FontStyle.Italic : FontStyle.Normal,
            font.B ? FontWeight.Bold : FontWeight.Normal);
        var decorations = new TextDecorationCollection();
        if (font.U) decorations.Add(TextDecorations.Underline[0]);
        if (font.Strike) decorations.Add(TextDecorations.Strikethrough[0]);
        return new TextLayout(key.Text, typeface, Math.Max(1, font.Sz),
            key.Foreground ?? ConditionalRenderer.Brush(font.Color),
            textAlignment: TextAlignment.Left, textWrapping: key.Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            textDecorations: decorations, maxWidth: key.Width);
    }

    internal void Clear()
    {
        foreach (var entry in _recent) entry.Dispose();
        _entries.Clear();
        _recent.Clear();
        _textUnits = 0;
        _gridPen = null;
        _context = null;
    }

    // Palette brushes are private, stable instances. Explicit font colors are part of CellFont's value key.
    private readonly record struct TextKey(string Text, CellFont Font, IBrush? Foreground, bool Wrap, double Width);

    private sealed class Entry(TextKey key, TextLayout layout, int textUnits) : IDisposable
    {
        internal TextKey Key { get; } = key;
        internal TextLayout? Layout { get; private set; } = layout;
        internal int TextUnits { get; } = textUnits;
        public void Dispose() { Layout?.Dispose(); Layout = null; }
    }

    internal readonly struct TextLease(TextLayout layout, bool ownsLayout) : IDisposable
    {
        internal TextLayout Layout { get; } = layout;
        public void Dispose() { if (ownsLayout) Layout.Dispose(); }
    }
}
