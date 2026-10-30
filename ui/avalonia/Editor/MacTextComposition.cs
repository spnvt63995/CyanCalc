using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace CyanCalc.Controls;

// Keep AppKit input ownership at the current native view boundary. Avalonia.Native
// 11.3.20 leaves Reset empty and sends modified keys to managed controls first.
internal static class MacTextComposition
{
    [ThreadStatic] private static bool _handlingOptionReturn;

    internal static bool HandleOptionReturn(CellEditor editor)
    {
        // AvnView sends modified keys to TextBox before inputContext. Give the
        // actual Option+Return event to IME first, so Korean Hanja can start.
        // A doCommandBySelector callback can re-enter KeyDown; that nested call
        // must take the ordinary TextBox path instead of forwarding again.
        if (_handlingOptionReturn || !OperatingSystem.IsMacOS() || !editor.IsFocused || editor.IsReadOnly ||
            TopLevel.GetTopLevel(editor)?.TryGetPlatformHandle() is not IMacOSTopLevelPlatformHandle handle)
            return false;
        var window = handle.NSWindow;
        var view = handle.NSView;
        if (window == 0 || view == 0 || SendObject(window, Selector("firstResponder")) != view) return false;
        var input = SendObject(window, Selector("currentEvent"));
        // NSEvent keyDown, Return/keypad Enter, Option without Shift/Control/Command.
        if (input == 0 || SendUInt(input, Selector("type")) != 10 ||
            SendObject(input, Selector("window")) != window ||
            SendUShort(input, Selector("keyCode")) is not (36 or 76) ||
            (SendUInt(input, Selector("modifierFlags")) & 0x1e0000) != 0x80000) return false;
        var context = SendObject(view, Selector("inputContext"));
        if (context == 0) return false;
        try
        {
            _handlingOptionReturn = true;
            return SendBool(context, Selector("handleEvent:"), input);
        }
        finally { _handlingOptionReturn = false; }
    }

    internal static bool Unmark(Visual editor)
    {
        if (!OperatingSystem.IsMacOS() ||
            TopLevel.GetTopLevel(editor)?.TryGetPlatformHandle() is not IMacOSTopLevelPlatformHandle handle)
            return false;
        var view = handle.NSView;
        if (view == 0) return false;
        var unmark = Selector("unmarkText");
        if (!SendBool(view, Selector("respondsToSelector:"), unmark) ||
            !SendBool(view, Selector("hasMarkedText"))) return false;
        // AvnView.unmarkText clears its marked range and calls inputContext.discardMarkedText.
        // It does not insert text; the owning CellEditor commits its captured preedit once.
        SendVoid(view, unmark);
        return true;
    }

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint SendObject(nint receiver, nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nuint SendUInt(nint receiver, nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern ushort SendUShort(nint receiver, nint selector);

    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    [DllImport(ObjC, EntryPoint = "sel_registerName")]
    private static extern nint Selector(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendBool(nint receiver, nint selector, nint argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendBool(nint receiver, nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(nint receiver, nint selector);
}
