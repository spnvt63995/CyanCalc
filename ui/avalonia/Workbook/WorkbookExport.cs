using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace CyanCalc.Controls;

public sealed partial class WorkbookView
{
    public event EventHandler? ExportPngRequested;

    /// <summary>Copies the current canvas into the full selection bounds, as the web PNG command does.</summary>
    public void ExportSelectionToPng(Stream output)
    {
        if (!CommitEdit()) throw new InvalidOperationException(Text["native.finish_edit_export"]);
        if (Snapshot is not { } snapshot || Session is not { } model) throw new InvalidOperationException(Text["native.no_workbook"]);
        var area = CellArea.FromView(snapshot.View);
        var start = SheetCoordinates.Position(model, snapshot, area.Row, area.Column);
        var end = SheetCoordinates.Position(model, snapshot, area.Row + area.Height, area.Column + area.Width);
        var crop = new Rect(start, end);
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(Worksheet.Bounds.Width * scale), (int)Math.Ceiling(Worksheet.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
        bitmap.Render(Worksheet);
        var visual = new ExportSlice(bitmap, crop);
        visual.Measure(crop.Size); visual.Arrange(new Rect(crop.Size));
        using var result = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(crop.Width * scale), (int)Math.Ceiling(crop.Height * scale)), new Vector(96 * scale, 96 * scale));
        result.Render(visual); result.Save(output);
    }
    private sealed class ExportSlice(Bitmap bitmap, Rect source) : Control
    {
        public override void Render(DrawingContext context)
        {
            // drawImage leaves pixels outside the source canvas transparent; it does not rescale the clipped part.
            var visible = source.Intersect(new Rect(bitmap.Size));
            if (visible.Width > 0 && visible.Height > 0)
                context.DrawImage(bitmap, visible, new Rect(new Point(visible.X - source.X, visible.Y - source.Y), visible.Size));
        }
    }
}
