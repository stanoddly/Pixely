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
    private static readonly string[] IgnoredErrors = ["No HID devices found in the system.", "No HID devices with requested VID/PID found in the system.", "Parameter 'table' is invalid"];

    public static void Clear()
    {
        SDL_ClearError();
    }
    
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
    
    public static void ThrowOnError()
    {
        string? error = GetError();
        if (string.IsNullOrEmpty(error))
        {
            return;
        }

        // an ugly hack: sometimes an error is randomly raised (from some random place?)
        // https://github.com/libsdl-org/SDL/blob/afa27243df76f61509d71041fcc8203b545c0388/src/hidapi/linux/hid.c#L1087
        foreach (string ignoredError in IgnoredErrors)
        {
            if (error == ignoredError)
            {
                Console.Error.WriteLine("IGNORED: " + error);
                return;
            }
        }

        throw new PixelyException(error);
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
