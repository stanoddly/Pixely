using System.Runtime.InteropServices;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely;

// An isolated app, such as a headless run, reports the Unknown theme and never raises ThemeChanged, so a scenario behaves the same
// whatever theme the host desktop uses. Locales stay readable there, since SDL_HINT_PREFERRED_LOCALES lets a scenario pin them.
// SDL_GetSystemTheme is main-thread only and SDL_GetPreferredLocales is not thread safe, so both members throw on another thread.
public sealed class PlatformSettings : IPlatformSettings
{
    private readonly bool _isolated;

    internal PlatformSettings(bool isolated)
    {
        _isolated = isolated;
    }

    public SystemTheme Theme
    {
        get
        {
            ThrowIfNotMainThread();
            return _isolated ? SystemTheme.Unknown : (SystemTheme)SDL_GetSystemTheme();
        }
    }

    public event SystemThemeChangedHandler? ThemeChanged;

    public event PreferredLocalesChangedHandler? PreferredLocalesChanged;

    // SDL reads the OS settings on the desktop and navigator.languages in the browser; SDL_HINT_PREFERRED_LOCALES overrides both.
    // The list can change while the app runs, so every call asks SDL again.
    public IReadOnlyList<Locale> GetPreferredLocales()
    {
        ThrowIfNotMainThread();
        unsafe
        {
            int count;
            Pointer<SDL_Locale>* locales = SDL_GetPreferredLocales(&count);
            // SDL returns null both when the platform reports no locales and when it fails to allocate the list.
            if (locales == null)
            {
                return [];
            }

            try
            {
                List<Locale> result = new(count);
                for (int i = 0; i < count; i++)
                {
                    result.Add(new Locale(Marshal.PtrToStringUTF8((nint)locales[i].Value.language)!, Marshal.PtrToStringUTF8((nint)locales[i].Value.country)));
                }

                return result;
            }
            finally
            {
                SDL_free((nint)locales);
            }
        }
    }

    internal void OnSystemThemeChanged(ulong timestamp)
    {
        if (!_isolated)
        {
            ThemeChanged?.Invoke(new SystemThemeChangedEventArgs(Theme, timestamp));
        }
    }

    internal void OnLocaleChanged(ulong timestamp)
    {
        PreferredLocalesChanged?.Invoke(new PreferredLocalesChangedEventArgs(GetPreferredLocales(), timestamp));
    }

    private static void ThrowIfNotMainThread()
    {
        if (!SDL_IsMainThread())
        {
            throw new PixelyException("Platform settings must be read from the main thread.");
        }
    }
}
