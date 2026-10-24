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
}
