using System.Runtime.InteropServices;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Input;

public class ClipboardService : IClipboardService
{
    public bool HasText
    {
        get
        {
            unsafe
            {
                return SDL_HasClipboardText();
            }
        }
    }

    public string? GetText()
    {
        // SDL allocates the text, never returns NULL and needs it freed; an empty string means no text or a failure.
        Pointer<byte> text = SDL_GetClipboardText();
        try
        {
            return Marshal.PtrToStringUTF8((nint)text);
        }
        finally
        {
            SDL_free((nint)text);
        }
    }

    public void SetText(string text)
    {
        using NullTerminatedUtf8 utf8Text = new NullTerminatedUtf8(text);
        SDL_SetClipboardText(utf8Text.Span);
    }
}
