namespace Pixely;

public readonly record struct SystemThemeChangedEventArgs(SystemTheme Theme, ulong Timestamp);

public delegate void SystemThemeChangedHandler(SystemThemeChangedEventArgs eventArgs);

public readonly record struct PreferredLocalesChangedEventArgs(IReadOnlyList<Locale> PreferredLocales, ulong Timestamp);

public delegate void PreferredLocalesChangedHandler(PreferredLocalesChangedEventArgs eventArgs);

// The user's settings of the operating system or the browser, which apply to the whole app and can change while it runs.
public interface IPlatformSettings
{
    SystemTheme Theme { get; }

    event SystemThemeChangedHandler? ThemeChanged;

    // The user's preferred locales, most preferred first; empty when the platform reports none.
    IReadOnlyList<Locale> GetPreferredLocales();

    event PreferredLocalesChangedHandler? PreferredLocalesChanged;
}
