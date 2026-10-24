namespace Pixely;

public interface ILocaleService
{
    // The user's preferred locales, most preferred first; empty when the platform reports none.
    IReadOnlyList<Locale> GetPreferredLocales();
}
