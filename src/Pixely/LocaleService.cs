using System.Runtime.InteropServices;
using SDL;

namespace Pixely;

// SDL reads the OS settings on the desktop and navigator.languages in the browser; SDL_HINT_PREFERRED_LOCALES overrides both.
// The list can change while the app runs, so every call asks SDL again.
public sealed class LocaleService : ILocaleService
{
    internal LocaleService()
    {
    }

    public IReadOnlyList<Locale> GetPreferredLocales()
    {
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
                Locale[] result = new Locale[count];
                for (int i = 0; i < count; i++)
                {
                    result[i] = new Locale(Marshal.PtrToStringUTF8((IntPtr)locales[i]->language)!, Marshal.PtrToStringUTF8((IntPtr)locales[i]->country));
                }

                return result;
            }
            finally
            {
                SDL3.SDL_free(locales);
            }
        }
    }
}
