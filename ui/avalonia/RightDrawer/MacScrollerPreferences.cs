using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace CyanCalc.Controls;

// Use AppKit's preference (including the pointing device), as WebKit does.
// The one native observer lives with the process; views subscribe only while attached.
internal static class MacScrollerPreferences
{
    internal static event Action? Changed;
    private static nint _observer;
    private static readonly NotificationCallback Callback = (_, _, _) => Dispatcher.UIThread.Post(() => Changed?.Invoke());

    internal static bool UsesOverlay
    {
        get
        {
            if (!OperatingSystem.IsMacOS()) return false;
            var scroller = GetClass("NSScroller");
            if (scroller == 0) return false; // Headless/non-AppKit host.
            Observe();
            return Send(scroller, Selector("preferredScrollerStyle")) == 1;
        }
    }

    private static void Observe()
    {
        if (_observer != 0) return;
        var type = AllocateClass(GetClass("NSObject"), "CyanCalcScrollerPreferenceObserver", 0);
        var selector = Selector("scrollerStyleChanged:");
        AddMethod(type, selector, Marshal.GetFunctionPointerForDelegate(Callback), "v@:@");
        RegisterClass(type);
        _observer = Send(Send(type, Selector("alloc")), Selector("init"));
        var name = InitString(Send(GetClass("NSString"), Selector("alloc")), Selector("initWithUTF8String:"), "NSPreferredScrollerStyleDidChangeNotification");
        var center = Send(GetClass("NSNotificationCenter"), Selector("defaultCenter"));
        AddObserver(center, Selector("addObserver:selector:name:object:"), _observer, selector, name, 0);
        Send(name, Selector("release"));
    }

    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NotificationCallback(nint self, nint command, nint notification);
    [DllImport(ObjC, EntryPoint = "objc_getClass")] private static extern nint GetClass(string name);
    [DllImport(ObjC, EntryPoint = "sel_registerName")] private static extern nint Selector(string name);
    [DllImport(ObjC, EntryPoint = "objc_allocateClassPair")] private static extern nint AllocateClass(nint superclass, string name, nuint extraBytes);
    [DllImport(ObjC, EntryPoint = "objc_registerClassPair")] private static extern void RegisterClass(nint type);
    [DllImport(ObjC, EntryPoint = "class_addMethod")]
    [return: MarshalAs(UnmanagedType.I1)] private static extern bool AddMethod(nint type, nint selector, nint implementation, string encoding);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint Send(nint receiver, nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint InitString(nint receiver, nint selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void AddObserver(nint receiver, nint selector, nint observer, nint callback, nint name, nint obj);
}
