using Pixely.App;
using Pixely.DependencyInjection;
using SDL;

namespace Pixely.Tests;

// SDL_HINT_PREFERRED_LOCALES replaces the OS settings, so the tests do not depend on the machine's language. The hint is set with
// override priority because an SDL_PREFERRED_LOCALES environment variable outranks a hint set at normal priority.
[NonParallelizable]
public class LocaleServiceTests
{
    private IPixelyApp _app = null!;
    private ILocaleService _localeService = null!;

    [SetUp]
    public void SetUp()
    {
        SDL3.SDL_SetHintWithPriority(SDL3.SDL_HINT_VIDEO_DRIVER, "dummy", SDL_HintPriority.SDL_HINT_DEFAULT);
        _app = new PixelyAppBuilder().Build();
        _localeService = ((PixelyApp)_app).ServiceProvider.GetRequiredService<ILocaleService>();
    }

    [TearDown]
    public void TearDown()
    {
        SDL3.SDL_ResetHint(SDL3.SDL_HINT_PREFERRED_LOCALES);
        _app.Dispose();
    }

    [Test]
    public void GetPreferredLocales_ReturnsLocalesInPreferenceOrder()
    {
        SetPreferredLocales("cs_CZ,en");

        Assert.That(_localeService.GetPreferredLocales(), Is.EqualTo(new[] { new Locale("cs", "CZ"), new Locale("en", null) }));
    }

    [Test]
    public void GetPreferredLocales_NoLocales_ReturnsEmpty()
    {
        SetPreferredLocales("");

        Assert.That(_localeService.GetPreferredLocales(), Is.Empty);
    }

    [TestCase("en,")]
    [TestCase("en, ")]
    public void GetPreferredLocales_TrailingEmptyItem_SkipsIt(string locales)
    {
        SetPreferredLocales(locales);

        Assert.That(_localeService.GetPreferredLocales(), Is.EqualTo(new[] { new Locale("en", null) }));
    }

    // SDL parses ",en" as the language ",en" followed by a null entry, which it still counts.
    [Test]
    public void GetPreferredLocales_LeadingComma_SkipsTheNullEntry()
    {
        SetPreferredLocales(",en");

        Assert.That(_localeService.GetPreferredLocales(), Is.EqualTo(new[] { new Locale(",en", null) }));
    }

    [Test]
    public void GetPreferredLocales_AfterTheListChanges_ReturnsTheNewList()
    {
        SetPreferredLocales("en_US");
        _ = _localeService.GetPreferredLocales();
        SetPreferredLocales("de");

        Assert.That(_localeService.GetPreferredLocales(), Is.EqualTo(new[] { new Locale("de", null) }));
    }

    private static void SetPreferredLocales(string locales)
    {
        bool set = SDL3.SDL_SetHintWithPriority(SDL3.SDL_HINT_PREFERRED_LOCALES, locales, SDL_HintPriority.SDL_HINT_OVERRIDE);
        Assert.That(set, Is.True);
    }
}
