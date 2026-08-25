using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.Utilities;

namespace Pixely.Sprites;

public sealed class AnimatedSpriteAssetLoader : IAnimatedSpriteAssetLoader
{
    private readonly ContentSource _contentSource;
    private readonly ITextureLoader _textureLoader;
    private readonly SpriteAssetStorage _storage;

    public AnimatedSpriteAssetLoader(ITextureLoader textureLoader, ContentSource contentSource, SpriteAssetStorage storage)
    {
        _textureLoader = textureLoader;
        _contentSource = contentSource;
        _storage = storage;
    }

    private AnimatedSpriteAsset CreateAnimation(AnimatedSpriteDto animatedSpriteDto)
    {
        Texture texture = _textureLoader.Load(animatedSpriteDto.Texture);
        ImmutableArray<ShortRectangle>.Builder builder = ImmutableArray.CreateBuilder<ShortRectangle>(animatedSpriteDto.Frames.Length);
        foreach (ShortRectangle frame in animatedSpriteDto.Frames)
        {
            builder.Add(frame);
        }
        Vector2 anchorOffset = animatedSpriteDto.AnchorOffset;
        AnimatedSpriteAsset animatedSpriteAsset = new AnimatedSpriteAsset((float)animatedSpriteDto.FrameDuration, texture, builder.MoveToImmutable(), anchorOffset, animatedSpriteDto.Flip);
        return animatedSpriteAsset;
    }

    public AnimatedSpriteAsset Load(ReadOnlySpan<char> path)
    {
        if (_storage.TryGetAnimatedSprite(path, out AnimatedSpriteAsset? existingAnimation))
        {
            return existingAnimation;
        }
        using Stream stream = _contentSource.OpenStream(path);
        AnimatedSpriteDto animatedSpriteDto = JsonSerializer.Deserialize(stream, SpriteDtosJsonContext.Default.AnimatedSpriteDto)
                                        ?? throw new JsonException("Deserialization returned null for AnimatedSpriteDto.");
        AnimatedSpriteAsset animatedSpriteAsset = CreateAnimation(animatedSpriteDto);
        _storage.StoreAnimatedSprite(path.ToString(), animatedSpriteAsset);
        return animatedSpriteAsset;
    }
}
