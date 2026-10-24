using Pixely.App;
using Pixely.DependencyInjection;
using SDL;

namespace Pixely.Tests;

// SDL_HINT_PREFERRED_LOCALES replaces the OS settings, so the tests do not depend on the machine's language.
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
        SDL3.SDL_SetHint(SDL3.SDL_HINT_PREFERRED_LOCALES, "cs_CZ,en");

        Assert.That(_localeService.GetPreferredLocales(), Is.EqualTo(new[] { new Locale("cs", "CZ"), new Locale("en", null) }));
    }

    [Test]
    public void GetPreferredLocales_NoLocales_ReturnsEmpty()
    {
        SDL3.SDL_SetHint(SDL3.SDL_HINT_PREFERRED_LOCALES, "");

        Assert.That(_localeService.GetPreferredLocales(), Is.Empty);
    }

    [Test]
    public void GetPreferredLocales_AfterTheListChanges_ReturnsTheNewList()
    {
        SDL3.SDL_SetHint(SDL3.SDL_HINT_PREFERRED_LOCALES, "en_US");
        _ = _localeService.GetPreferredLocales();
        SDL3.SDL_SetHint(SDL3.SDL_HINT_PREFERRED_LOCALES, "de");

        Assert.That(_localeService.GetPreferredLocales(), Is.EqualTo(new[] { new Locale("de", null) }));
    }
}
