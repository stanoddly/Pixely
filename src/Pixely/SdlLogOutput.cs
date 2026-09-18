using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SDL;

namespace Pixely;

// SDL writes every log message to standard error, which a browser console shows as an error. Installed, the messages go
// to the application's logger or, without one, to the console by priority. SDL's log output function is process-wide,
// hence the static state.
internal static class SdlLogOutput
{
    private static ILogger? _logger;

    internal static unsafe void Install(ILogger? logger)
    {
        _logger = logger;
        SDL3.SDL_SetLogOutputFunction(&OnMessage, IntPtr.Zero);
    }

    internal static unsafe void Uninstall()
    {
        SDL3.SDL_SetLogOutputFunction(SDL3.SDL_GetDefaultLogOutputFunction(), IntPtr.Zero);
        _logger = null;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnMessage(IntPtr userdata, int category, SDL_LogPriority priority, byte* message)
    {
        try
        {
            Write(priority, Marshal.PtrToStringUTF8((IntPtr)message) ?? string.Empty);
        }
        catch
        {
            // An exception must not cross into SDL.
        }
    }

    internal static void Write(SDL_LogPriority priority, string message)
    {
        // Read once: Uninstall clears the field while an SDL thread may still be logging.
        ILogger? logger = _logger;
        if (logger != null)
        {
            logger.Log(ToLogLevel(priority), "{SdlMessage}", message);
            return;
        }

        switch (priority)
        {
            case SDL_LogPriority.SDL_LOG_PRIORITY_CRITICAL:
                Console.Error.WriteLine("CRITICAL: " + message);
                break;
            case SDL_LogPriority.SDL_LOG_PRIORITY_ERROR:
                Console.Error.WriteLine("ERROR: " + message);
                break;
            case SDL_LogPriority.SDL_LOG_PRIORITY_WARN:
                Console.Error.WriteLine("WARNING: " + message);
                break;
            default:
                Console.Out.WriteLine(message);
                break;
        }
    }

    internal static LogLevel ToLogLevel(SDL_LogPriority priority)
    {
        return priority switch
        {
            SDL_LogPriority.SDL_LOG_PRIORITY_CRITICAL => LogLevel.Critical,
            SDL_LogPriority.SDL_LOG_PRIORITY_ERROR => LogLevel.Error,
            SDL_LogPriority.SDL_LOG_PRIORITY_WARN => LogLevel.Warning,
            SDL_LogPriority.SDL_LOG_PRIORITY_INFO => LogLevel.Information,
            SDL_LogPriority.SDL_LOG_PRIORITY_DEBUG => LogLevel.Debug,
            _ => LogLevel.Trace,
        };
    }
}
