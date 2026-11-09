using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely;

public enum MessageBoxSeverity
{
    Information,
    Warning,
    Error
}

/// <summary>
/// Presents native modal message boxes that do not require a window.
/// </summary>
/// <remarks>
/// These boxes are usable before SDL is initialized and after a window has been destroyed, which
/// makes them suitable for reporting failures that leave nothing able to render. Use
/// <see cref="Window.ShowModalMessageBox"/> instead when a window is available, so the box is
/// parented to it and frame timing is suspended while it is open.
/// </remarks>
public static class MessageBox
{
    public static void Show(MessageBoxSeverity severity, string title, string message)
    {
        unsafe
        {
            Show(severity, title, message, null);
        }
    }

    internal static unsafe void Show(MessageBoxSeverity severity, string title, string message, SDL_Window* window)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(message);

        using NullTerminatedUtf8 utf8Title = new NullTerminatedUtf8(title);
        using NullTerminatedUtf8 utf8Message = new NullTerminatedUtf8(message);
        SdlError.ThrowOnFalse(SDL_ShowSimpleMessageBox(ToFlags(severity), utf8Title.Span, utf8Message.Span, window), nameof(SDL_ShowSimpleMessageBox));
    }

    private static SDL_MessageBoxFlags ToFlags(MessageBoxSeverity severity)
    {
        return severity switch
        {
            MessageBoxSeverity.Information => SDL_MessageBoxFlags.SDL_MESSAGEBOX_INFORMATION,
            MessageBoxSeverity.Warning => SDL_MessageBoxFlags.SDL_MESSAGEBOX_WARNING,
            MessageBoxSeverity.Error => SDL_MessageBoxFlags.SDL_MESSAGEBOX_ERROR,
            _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null)
        };
    }
}
