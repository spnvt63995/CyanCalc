using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CyanCalc;

/// <summary>Bindings for IronCalc's module-level column and sheet-reference helpers.</summary>
public static partial class ReferenceUtils
{
    private static T Call<T>(string operation, object? arguments = null)
    {
        var request = arguments is null ? new JsonObject() : JsonSerializer.SerializeToNode(arguments, UserModel.JsonOptions)!.AsObject();
        request["op"] = operation;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, UserModel.JsonOptions);
        var pointer = NativeMethods.CallUtility(bytes, (nuint)bytes.Length);
        if (pointer == 0) throw new InvalidOperationException("IronCalc returned an empty native response.");
        try
        {
            using var response = JsonDocument.Parse(Marshal.PtrToStringUTF8(pointer)!);
            var root = response.RootElement;
            if (!root.GetProperty("ok").GetBoolean())
                throw new IronCalcException(operation, root.GetProperty("error").GetString() ?? "Unknown engine error");
            return root.GetProperty("value").Deserialize<T>(UserModel.JsonOptions)
                ?? throw new InvalidOperationException($"No {typeof(T).Name} returned by {operation}");
        }
        finally { NativeMethods.FreeString(pointer); }
    }
}
