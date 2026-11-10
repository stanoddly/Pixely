using System.Runtime.InteropServices;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely;

public class PixelyException : Exception
{
    public PixelyException()
    {
    }

    public PixelyException(string? message)
        : base(message)
    {
    }

    public PixelyException(string? message, Exception? inner)
        : base(message, inner)
    {
    }
}

internal static class SdlError
{
    public static void ThrowOnFalse(bool value)
    {
        if (!value) throw new PixelyException(GetError());
    }

    public static void ThrowOnFalse(bool value, string context)
    {
        if (!value)
        {
            Throw(context);
        }
    }

    public static void ThrowOnNull<T>(Pointer<T> pointer, string context) where T : unmanaged
    {
        if (!pointer.IsNull)
        {
            return;
        }

        Throw(context);
    }
    
    public static void ThrowOnNull<T>(Pointer<T> pointer) where T : unmanaged
    {
        if (pointer.IsNull) throw new PixelyException(GetError());
    }
    
    public static unsafe void ThrowOnNull<T>(T* pointer) where T : unmanaged
    {
        if (pointer == null) throw new PixelyException(GetError());
    }
    
    public static void Throw(string context)
    {
        string? error = GetError();
        if (string.IsNullOrEmpty(error))
        {
            error = $"Error happened in {context}, SDL_GetError is empty";
        }
        else
        {
            error = $"Error happened in {context}, SDL_GetError returned: {error}";
        }

        throw new PixelyException(error);
    }

    internal static string? GetError() => Marshal.PtrToStringUTF8((nint)SDL_GetError());
}
