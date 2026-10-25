using System.Runtime.InteropServices;
using SDL;

namespace Pixely;

// A headless run reports the Unknown theme and never raises ThemeChanged, so a scenario behaves the same whatever theme the host
// desktop uses. Locales stay readable there, since SDL_HINT_PREFERRED_LOCALES lets a scenario pin them.
// SDL_GetSystemTheme is main-thread only and SDL_GetPreferredLocales is not thread safe, so both members throw on another thread.
public sealed class PlatformSettings : IPlatformSettings
{
    private readonly bool _headless;

    internal PlatformSettings(bool headless)
    {
        _headless = headless;
    }

    public SystemTheme Theme
    {
        get
        {
            ThrowIfNotMainThread();
            return _headless ? SystemTheme.Unknown : (SystemTheme)SDL3.SDL_GetSystemTheme();
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
            SDL_Locale** locales = SDL3.SDL_GetPreferredLocales(&count);
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
                    // SDL counts every comma-separated item, but an empty one such as in "en," or ",en" comes back as a null language or a
                    // null entry, which would crash the process on dereference.
                    string? language = locales[i] == null ? null : Marshal.PtrToStringUTF8((IntPtr)locales[i]->language);
                    if (string.IsNullOrEmpty(language))
                    {
                        continue;
                    }

                    result.Add(new Locale(language, Marshal.PtrToStringUTF8((IntPtr)locales[i]->country)));
                }

                return result;
            }
            finally
            {
                SDL3.SDL_free(locales);
            }
        }
    }

    internal void OnSystemThemeChanged(ulong timestamp)
    {
        if (!_headless)
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
        if (!SDL3.SDL_IsMainThread())
        {
            throw new PixelyException("Platform settings must be read from the main thread.");
        }
    }
}
