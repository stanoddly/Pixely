using Pixely.Gpu;
using Pixely.Text;
using Pixely.Utilities;
using SDL;

namespace Pixely.Tests;

public sealed class TextSpriteAssetTests
{
    [Test]
    public void Type_DoesNotExposeResourceOwnership()
    {
        Assert.That(typeof(IDisposable).IsAssignableFrom(typeof(TextSpriteAsset)), Is.False);
    }

    [Test]
    public unsafe void BorrowedTexture_DisposeDoesNotInvalidateSharedAssets()
    {
        Pointer<SDL_GPUTexture> pointer = (SDL_GPUTexture*)1;
        TestTexture backingTexture = new TestTexture(pointer);
        BorrowedTexture borrowedTexture = new BorrowedTexture(backingTexture);
        TextSpriteAsset first = new TextSpriteAsset(borrowedTexture, new ShortRectangle(0, 0, 16, 10));
        TextSpriteAsset second = new TextSpriteAsset(borrowedTexture, new ShortRectangle(0, 0, 16, 10));

        first.Texture.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(first.Texture.IsDisposed, Is.False);
            Assert.That(second.Texture.IsDisposed, Is.False);
        });
    }

    [Test]
    public unsafe void Invalidate_InvalidatesAllSharedAssets()
    {
        Pointer<SDL_GPUTexture> pointer = (SDL_GPUTexture*)1;
        TestTexture backingTexture = new TestTexture(pointer);
        BorrowedTexture borrowedTexture = new BorrowedTexture(backingTexture);
        TextSpriteAsset first = new TextSpriteAsset(borrowedTexture, new ShortRectangle(0, 0, 16, 10));
        TextSpriteAsset second = new TextSpriteAsset(borrowedTexture, new ShortRectangle(0, 0, 16, 10));

        borrowedTexture.Invalidate();

        Assert.Multiple(() =>
        {
            Assert.That(first.Texture.IsDisposed, Is.True);
            Assert.That(second.Texture.IsDisposed, Is.True);
        });
    }

    private sealed class TestTexture : Texture
    {
        internal TestTexture(Pointer<SDL_GPUTexture> pointer)
            : base(pointer, new ShortSize(16, 10), TextureFormat.R8G8B8A8Unorm, 640)
        {
        }

        public override void Dispose()
        {
        }
    }
}
