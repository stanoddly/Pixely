# Preferred locales

`ILocaleService` returns the user's preferred locales, most preferred first:

```csharp
public sealed class LanguagePicker(ILocaleService localeService)
{
    public string PickLanguage()
    {
        foreach (Locale locale in localeService.GetPreferredLocales())
        {
            if (locale.Language is "cs" or "en")
            {
                return locale.Language;
            }
        }

        return "en";
    }
}
```

- `Language` is an ISO 639 code such as `cs` or `en`.
- `Country` is an ISO 3166 code such as `CZ`, or null when the locale names no country.
- The list is empty when the platform reports no locales.

## Why not `CultureInfo`

A Pixely game usually sets `InvariantGlobalization`. It keeps the ICU data out of the browser download and drops the need for `libicu` on Linux. With it set, `CultureInfo.CurrentUICulture` is always the invariant culture, so it cannot tell the player's language.

## Platform behavior

- On the desktop, SDL reads the operating system's language settings.
- In the browser, SDL reads `navigator.languages`.
- It needs no window, so it works in headless mode too.
- The SDL hint `SDL_HINT_PREFERRED_LOCALES` replaces the platform's list, for example `cs_CZ,en`. Tests use it to stay independent of the machine's language.

The list can change while the app runs. Every call asks SDL again, so a later call returns the new list. Pixely raises no event when the list changes.

References:

- [SDL_GetPreferredLocales](https://wiki.libsdl.org/SDL3/SDL_GetPreferredLocales)
