# Preferred locales

`ILocaleService` returns the user's preferred locales. It is meant for games that set `InvariantGlobalization`, where .NET cannot tell the player's language.

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

- `Language` is usually an ISO 639 code such as `cs` or `en`.
- `Country` is usually an ISO 3166 code such as `CZ`, or null when the locale names no country.
- Pixely does not validate either. For example, a browser reporting `es-419` gives the country `419`.
- The list is in the platform's order of preference, which is not always the user's. See the Linux note below.
- The list is empty when the platform reports no locales.

## Why not `CultureInfo`

`InvariantGlobalization` keeps the ICU data out of the browser download and drops the need for `libicu` on Linux. With it set:

- `CultureInfo.CurrentUICulture` starts as the invariant culture and never reflects the operating system's language.
- `new CultureInfo("cs-CZ")` throws `CultureNotFoundException`.
- With `PredefinedCulturesOnly` set to false, the culture can be created and keeps its name, but its culture-specific data is invariant. For example, `TwoLetterISOLanguageName` is `iv`.

A game without `InvariantGlobalization` can use `CultureInfo.CurrentUICulture` instead.

## Platform behavior

- On the desktop, SDL reads the operating system's language settings.
- On Linux, SDL reads `LANG` first and then `LANGUAGE`, and ignores `LC_ALL` and `LC_MESSAGES`. A user with `LANG=en_US.UTF-8` and `LANGUAGE=cs_CZ:en_US` gets `en_US, cs_CZ, en_US`. That puts English first and repeats it.
- In the browser, SDL reads `navigator.languages`.
- It needs no window, so it works in headless mode too.
- SDL keeps at most 127 bytes of the list, so a long list loses its tail. In the browser, SDL drops the entries that do not fit. Elsewhere, such as on Linux or through the hint, the last entry can be cut short.

## Overriding the list

The SDL hint `SDL_HINT_PREFERRED_LOCALES` replaces the platform's list, for example `cs_CZ,en`. SDL also reads it from the `SDL_PREFERRED_LOCALES` environment variable, which outranks a hint set at normal priority. Set the hint with `SDL_HINT_OVERRIDE` priority to win over the environment.

Pixely skips entries that SDL returns without a language, so `en,` gives only `en`. A leading comma is not repaired: SDL parses `,en` as the language `,en`.

## Changes while the app runs

The list can change while the app runs. Every call asks SDL again, so a later call returns the new list. Pixely raises no event when the list changes.

References:

- [SDL_GetPreferredLocales](https://wiki.libsdl.org/SDL3/SDL_GetPreferredLocales)
- [.NET globalization invariant mode](https://aka.ms/GlobalizationInvariantMode)
