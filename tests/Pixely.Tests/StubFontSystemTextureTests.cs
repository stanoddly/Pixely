using Pixely.Gpu;
using Pixely.Testing;
using Pixely.Text;

namespace Pixely.Tests;

// In Pixely.Tests, which sees whether a texture is disposed.
public class StubFontSystemTextureTests
{
    [Test]
    public void DisposingASpritesTexture_LeavesTheCachedSpriteUsable()
    {
        using StubFontSystem fontSystem = new();
        Font font = fontSystem.Load("fonts/a.ttf", 20);
        TextSpriteAsset sprite = font.CreateTextSprite("Score");

        sprite.Texture.Dispose();

        Assert.That((ReferenceEquals(font.CreateTextSprite("Score"), sprite), sprite.Texture.IsDisposed), Is.EqualTo((true, false)));
    }

    [Test]
    public void Dispose_InvalidatesTheSpritesTextures()
    {
        StubFontSystem fontSystem = new();
        TextSpriteAsset sprite = fontSystem.Load("fonts/a.ttf", 20).CreateTextSprite("Score");

        fontSystem.Dispose();

        Assert.That(sprite.Texture.IsDisposed, Is.True);
    }
}
