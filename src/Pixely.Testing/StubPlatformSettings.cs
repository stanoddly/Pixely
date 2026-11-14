namespace Pixely.Testing;

// Settings of no platform: the theme is unknown, no locale is preferred, and neither ever changes.
internal sealed class StubPlatformSettings : IPlatformSettings
{
    public SystemTheme Theme => SystemTheme.Unknown;

    public event SystemThemeChangedHandler? ThemeChanged
    {
        add { }
        remove { }
    }

    public IReadOnlyList<Locale> GetPreferredLocales() => [];

    public event PreferredLocalesChangedHandler? PreferredLocalesChanged
    {
        add { }
        remove { }
    }
}
