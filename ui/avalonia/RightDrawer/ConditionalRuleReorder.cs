using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace CyanCalc.Controls;

// ConditionalFormatting.tsx / ic-cf-slide: translate both rows, then commit once.
internal sealed class ConditionalRuleReorder
{
    private Action<bool>? _complete;
    internal bool IsRunning => _complete is not null;
    internal void Finish() => _complete?.Invoke(true);
    internal void Cancel() => _complete?.Invoke(false);

    internal void Start(Control row, Control? neighbor, bool lower, bool reducedMotion, Action commit)
    {
        if (IsRunning) return;
        if (neighbor is null || reducedMotion || TopLevel.GetTopLevel(row) is not { } topLevel)
        {
            commit(); return;
        }

        var rowShift = (lower ? 1 : -1) * neighbor.Bounds.Height;
        var neighborShift = (lower ? -1 : 1) * row.Bounds.Height;
        var rowTransform = row.RenderTransform; var neighborTransform = neighbor.RenderTransform;
        var rowHitTest = row.IsHitTestVisible; var neighborHitTest = neighbor.IsHitTestVisible;
        var movingRow = new TranslateTransform(); var movingNeighbor = new TranslateTransform();
        // CSS ease-in-out, not the similarly named sine or quadratic easing.
        var easing = new SplineEasing { X1 = .42, Y1 = 0, X2 = .58, Y2 = 1 };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        TimeSpan? started = null;
        var finished = false;
        void Complete(bool apply)
        {
            if (finished) return;
            finished = true; _complete = null; timer.Stop();
            row.DetachedFromVisualTree -= Detached; neighbor.DetachedFromVisualTree -= Detached;
            // Commit and remove the transforms in the same UI turn, as flushSync does.
            try { if (apply) commit(); }
            finally
            {
                row.RenderTransform = rowTransform; neighbor.RenderTransform = neighborTransform;
                row.IsHitTestVisible = rowHitTest; neighbor.IsHitTestVisible = neighborHitTest;
            }
        }
        void Detached(object? sender, VisualTreeAttachmentEventArgs args) => Complete(true);
        void Frame(TimeSpan now)
        {
            if (finished) return;
            started ??= now;
            var progress = Math.Clamp((now - started.Value).TotalMilliseconds / 200, 0, 1);
            var offset = easing.Ease(progress);
            movingRow.Y = rowShift * offset; movingNeighbor.Y = neighborShift * offset;
            if (progress >= 1) Complete(true);
            else topLevel.RequestAnimationFrame(Frame);
        }
        _complete = Complete;
        row.RenderTransform = movingRow; neighbor.RenderTransform = movingNeighbor;
        row.IsHitTestVisible = neighbor.IsHitTestVisible = false;
        row.DetachedFromVisualTree += Detached; neighbor.DetachedFromVisualTree += Detached;
        // Upstream also has a 200 + 100ms fallback when animation events do not arrive.
        timer.Tick += (_, _) => Complete(true); timer.Start();
        topLevel.RequestAnimationFrame(Frame);
    }

    // Avalonia 11 has no reduced-motion setting. Query AppKit at each request so
    // macOS accessibility changes take effect without restarting the workbook.
    internal static bool PrefersReducedMotion
    {
        get
        {
            if (!OperatingSystem.IsMacOS()) return false;
            var type = GetClass("NSWorkspace");
            if (type == 0) return false; // No AppKit host (for example, headless rendering).
            var workspace = SendObject(type, Selector("sharedWorkspace"));
            return workspace != 0 && SendBool(workspace, Selector("accessibilityDisplayShouldReduceMotion"));
        }
    }
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    [DllImport(ObjC, EntryPoint = "objc_getClass")]
    private static extern nint GetClass(string name);
    [DllImport(ObjC, EntryPoint = "sel_registerName")]
    private static extern nint Selector(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint SendObject(nint receiver, nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendBool(nint receiver, nint selector);
}
