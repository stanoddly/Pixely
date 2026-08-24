using System.Numerics;
using System.Text.Json;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.Utilities;

namespace Pixely.Sprites;

public sealed class SpriteAssetLoader : ISpriteAssetLoader
{
    private readonly ContentSource _contentSource;
    private readonly ITextureLoader _textureLoader;
    private readonly SpriteAssetStorage _storage;

    public SpriteAssetLoader(ITextureLoader textureLoader, ContentSource contentSource, SpriteAssetStorage storage)
    {
        _contentSource = contentSource;
        _textureLoader = textureLoader;
        _storage = storage;
    }

    public SpriteAsset Load(ReadOnlySpan<char> path)
    {
        if (_storage.TryGetSprite(path, out SpriteAsset? existingSprite))
        {
            return existingSprite;
        }

        using Stream spritesJsonStream = _contentSource.OpenStream(path);

        SpriteDto spriteDto = JsonSerializer.Deserialize(spritesJsonStream, SpriteDtosJsonContext.Default.SpriteDto)
                              ?? throw new JsonException("Deserialization returned null for SpriteDto.");

        Texture texture = _textureLoader.Load(spriteDto.Texture);
        ShortRectangle imageRegion = spriteDto.TextureRegion;
        Vector2 anchorOffset = spriteDto.AnchorOffset;

        SpriteAsset spriteAsset = new SpriteAsset(texture, imageRegion, spriteDto.Flip)
        {
            AnchorOffset = anchorOffset,
        };

        _storage.StoreSprite(path.ToString(), spriteAsset);

        return spriteAsset;
    }
}
