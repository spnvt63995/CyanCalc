using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CyanCalc;

internal sealed class WorkbookHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public WorkbookHandle() : base(true) { }
    protected override bool ReleaseHandle()
    {
        NativeMethods.Free(handle);
        return true;
    }
}

internal static class NativeMethods
{
    private const string Library = "cyancalc_native";

    [DllImport(Library, EntryPoint = "ic_model_new", CallingConvention = CallingConvention.Cdecl)]
    internal static extern WorkbookHandle Create();

    [DllImport(Library, EntryPoint = "ic_model_free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Free(nint handle);

    [DllImport(Library, EntryPoint = "ic_model_call", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint Call(WorkbookHandle handle, byte[] request, nuint length);

    [DllImport(Library, EntryPoint = "ic_utility_call", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint CallUtility(byte[] request, nuint length);

    [DllImport(Library, EntryPoint = "ic_string_free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void FreeString(nint response);
}
