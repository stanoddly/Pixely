using Pixely.App;
using Pixely.DependencyInjection;
using SDL;

namespace Pixely.Tests;

// Builds real apps with SDL's dummy video driver. SDL_HINT_PREFERRED_LOCALES replaces the OS settings, so the tests do not depend on the
// machine's language. The hint is set with override priority because an SDL_PREFERRED_LOCALES environment variable outranks a hint set
// at normal priority. A test cannot change the desktop theme, so the theme tests push SDL's theme event themselves.
[NonParallelizable]
public class PlatformSettingsTests
{
    private IPixelyApp? _app;

    [SetUp]
    public void SetUp()
    {
        SDL3.SDL_SetHintWithPriority(SDL3.SDL_HINT_VIDEO_DRIVER, "dummy", SDL_HintPriority.SDL_HINT_DEFAULT);
    }

    [TearDown]
    public void TearDown()
    {
        SDL3.SDL_ResetHint(SDL3.SDL_HINT_PREFERRED_LOCALES);
        _app?.Dispose();
    }

    [Test]
    public void Theme_ReportsSdlSystemTheme()
    {
        IPlatformSettings settings = Build(new PixelyConfig()).GetRequiredService<IPlatformSettings>();

        Assert.That(settings.Theme, Is.EqualTo((SystemTheme)SDL3.SDL_GetSystemTheme()));
    }

    [Test]
    public void ThemeChanged_SdlThemeEvent_RaisesWithCurrentTheme()
    {
        ServiceProvider provider = Build(new PixelyConfig());
        IPlatformSettings settings = provider.GetRequiredService<IPlatformSettings>();
        List<SystemThemeChangedEventArgs> raised = new();
        settings.ThemeChanged += raised.Add;

        Push(SDL_EventType.SDL_EVENT_SYSTEM_THEME_CHANGED);
        provider.GetRequiredService<EventService>().Process();

        Assert.That(raised, Has.Count.EqualTo(1));
        Assert.That(raised[0].Theme, Is.EqualTo(settings.Theme));
    }

    [Test]
    public void Isolated_ReportsUnknownThemeAndIgnoresThemeEvent()
    {
        PixelyConfig config = new() { IsIsolated = true };
        ServiceProvider provider = Build(config);
        IPlatformSettings settings = provider.GetRequiredService<IPlatformSettings>();
        int raised = 0;
        settings.ThemeChanged += _ => raised++;

        Push(SDL_EventType.SDL_EVENT_SYSTEM_THEME_CHANGED);
        provider.GetRequiredService<EventService>().Process();

        Assert.That(settings.Theme, Is.EqualTo(SystemTheme.Unknown));
        Assert.That(raised, Is.Zero);
    }

    [Test]
    public void GetPreferredLocales_ReturnsLocalesInPreferenceOrder()
    {
        IPlatformSettings settings = Build(new PixelyConfig()).GetRequiredService<IPlatformSettings>();
        SetPreferredLocales("cs_CZ,en");

        Assert.That(settings.GetPreferredLocales(), Is.EqualTo(new[] { new Locale("cs", "CZ"), new Locale("en", null) }));
    }

    [Test]
    public void GetPreferredLocales_NoLocales_ReturnsEmpty()
    {
        IPlatformSettings settings = Build(new PixelyConfig()).GetRequiredService<IPlatformSettings>();
        SetPreferredLocales("");

        Assert.That(settings.GetPreferredLocales(), Is.Empty);
    }

    [TestCase("en,")]
    [TestCase("en, ")]
    public void GetPreferredLocales_TrailingEmptyItem_SkipsIt(string locales)
    {
        IPlatformSettings settings = Build(new PixelyConfig()).GetRequiredService<IPlatformSettings>();
        SetPreferredLocales(locales);

        Assert.That(settings.GetPreferredLocales(), Is.EqualTo(new[] { new Locale("en", null) }));
    }

    // SDL parses ",en" as the language ",en" followed by a null entry, which it still counts.
    [Test]
    public void GetPreferredLocales_LeadingComma_SkipsTheNullEntry()
    {
        IPlatformSettings settings = Build(new PixelyConfig()).GetRequiredService<IPlatformSettings>();
        SetPreferredLocales(",en");

        Assert.That(settings.GetPreferredLocales(), Is.EqualTo(new[] { new Locale(",en", null) }));
    }

    [Test]
    public void GetPreferredLocales_AfterTheListChanges_ReturnsTheNewList()
    {
        IPlatformSettings settings = Build(new PixelyConfig()).GetRequiredService<IPlatformSettings>();
        SetPreferredLocales("en_US");
        _ = settings.GetPreferredLocales();
        SetPreferredLocales("de");

        Assert.That(settings.GetPreferredLocales(), Is.EqualTo(new[] { new Locale("de", null) }));
    }

    [Test]
    public void Theme_OffTheMainThread_Throws()
    {
        IPlatformSettings settings = Build(new PixelyConfig()).GetRequiredService<IPlatformSettings>();

        Assert.Throws<PixelyException>(() => Task.Run(() => settings.Theme).GetAwaiter().GetResult());
    }

    [Test]
    public void GetPreferredLocales_OffTheMainThread_Throws()
    {
        IPlatformSettings settings = Build(new PixelyConfig()).GetRequiredService<IPlatformSettings>();

        Assert.Throws<PixelyException>(() => Task.Run(settings.GetPreferredLocales).GetAwaiter().GetResult());
    }

    // SDL sends no locale event when the hint changes, so the test pushes the one a platform such as macOS sends.
    [Test]
    public void PreferredLocalesChanged_SdlLocaleEvent_RaisesWithTheNewList()
    {
        ServiceProvider provider = Build(new PixelyConfig());
        IPlatformSettings settings = provider.GetRequiredService<IPlatformSettings>();
        List<PreferredLocalesChangedEventArgs> raised = new();
        settings.PreferredLocalesChanged += raised.Add;
        SetPreferredLocales("de");

        Push(SDL_EventType.SDL_EVENT_LOCALE_CHANGED);
        provider.GetRequiredService<EventService>().Process();

        Assert.That(raised, Has.Count.EqualTo(1));
        Assert.That(raised[0].PreferredLocales, Is.EqualTo(new[] { new Locale("de", null) }));
    }

    private ServiceProvider Build(PixelyConfig config)
    {
        PixelyAppBuilder builder = new();
        builder.AddSingleton(config);
        _app = builder.Build();
        return ((PixelyApp)_app).ServiceProvider;
    }

    private static void SetPreferredLocales(string locales)
    {
        bool set = SDL3.SDL_SetHintWithPriority(SDL3.SDL_HINT_PREFERRED_LOCALES, locales, SDL_HintPriority.SDL_HINT_OVERRIDE);
        Assert.That(set, Is.True);
    }

    private static void Push(SDL_EventType type)
    {
        SDL_Event evt = default;
        evt.type = (uint)type;
        bool pushed;
        unsafe
        {
            pushed = SDL3.SDL_PushEvent(&evt);
        }

        Assert.That(pushed, Is.True, () => SDL3.SDL_GetError() ?? string.Empty);
    }
}
