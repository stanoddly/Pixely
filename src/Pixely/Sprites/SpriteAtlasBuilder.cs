using System.Numerics;
using System.Text.Json;
using Pixely.Content;
using Pixely.Gpu;

namespace Pixely.Sprites;

public record SpriteAtlasBuilderConfig(string[] Directories);

public sealed class SpriteAtlasBuilder
{
    private const int Padding = 2;

    private readonly record struct PackedRectangle(ShortRectangle Rectangle, int ImageIndex);

    private readonly ContentSource _contentSource;
    private readonly ITextureLoader _textureLoader;
    private readonly IImageLoader _imageLoader;
    private readonly SpriteAssetStorage _storage;

    public static SpriteAtlasBuilder Create(
        SpriteAtlasBuilderConfig spriteAtlasBuilderConfig,
        ITextureLoader textureLoader,
        IImageLoader contentLoader,
        ContentSource contentSource,
        SpriteAssetStorage storage)
    {
        SpriteAtlasBuilder spriteAtlasBuilder = new(textureLoader, contentLoader, contentSource, storage);
        spriteAtlasBuilder.BuildSprites(spriteAtlasBuilderConfig.Directories);
        return spriteAtlasBuilder;
    }

    internal SpriteAtlasBuilder(ITextureLoader textureLoader, IImageLoader imageLoader, ContentSource contentSource, SpriteAssetStorage storage)
    {
        _textureLoader = textureLoader;
        _imageLoader = imageLoader;
        _contentSource = contentSource;
        _storage = storage;
    }

    public void BuildSprites(params string[] directories)
    {
        var entries = new List<(string path, string texturePath, ShortRectangle region, SpriteFlip flip,
            bool isAnimatedFrame, string? animationPath, double frameDuration,
            int frameIndex, int totalFrames)>();
        var animatedSpriteInfos = new Dictionary<string, (double frameDuration, SpriteFlip flip, List<int> frameIndices)>();

        foreach (var directory in directories)
        {
            CollectSpritesRecursively(directory, entries, animatedSpriteInfos);
        }

        if (entries.Count == 0)
        {
            return;
        }

        // Deduplicate by (texturePath, region) — flipped sprites share atlas space
        var dedupMap = new Dictionary<(string texturePath, ShortRectangle region), int>();
        var uniqueImages = new List<(Image image, ShortRectangle region)>();
        var entryToUnique = new int[entries.Count];

        for (int i = 0; i < entries.Count; i++)
        {
            ShortRectangle region = entries[i].region;
            var key = (entries[i].texturePath, region);
            if (!dedupMap.TryGetValue(key, out int uniqueIndex))
            {
                uniqueIndex = uniqueImages.Count;
                Image image = _imageLoader.Load(entries[i].texturePath);
                uniqueImages.Add((image, region));
                dedupMap[key] = uniqueIndex;
            }
            entryToUnique[i] = uniqueIndex;
        }

        // Sort unique images by area descending for packing
        var sortedIndices = Enumerable.Range(0, uniqueImages.Count).ToList();
        sortedIndices.Sort((a, b) =>
        {
            int areaA = uniqueImages[a].region.Width * uniqueImages[a].region.Height;
            int areaB = uniqueImages[b].region.Width * uniqueImages[b].region.Height;
            return areaB.CompareTo(areaA);
        });

        // Build a reordered list for packing and a mapping back
        var sortedImages = new List<(Image image, ShortRectangle region)>(uniqueImages.Count);
        var sortedToOriginal = new int[uniqueImages.Count];
        var originalToSorted = new int[uniqueImages.Count];
        for (int i = 0; i < sortedIndices.Count; i++)
        {
            sortedToOriginal[i] = sortedIndices[i];
            originalToSorted[sortedIndices[i]] = i;
            sortedImages.Add(uniqueImages[sortedIndices[i]]);
        }

        // Pack
        (ShortSize atlasSize, List<PackedRectangle> packedRectangles) = PackImagesIntoAtlas(sortedImages);

        // Create atlas image
        RawImage atlasImage = CreateAtlasImage(sortedImages, packedRectangles, atlasSize);
        Texture atlasTexture = _textureLoader.Load(atlasImage);

        // Store static sprites
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.isAnimatedFrame)
            {
                continue;
            }

            int uniqueIndex = entryToUnique[i];
            int sortedIndex = originalToSorted[uniqueIndex];
            PackedRectangle packed = packedRectangles[sortedIndex];

            short atlasX = (short)(packed.Rectangle.X + Padding);
            short atlasY = (short)(packed.Rectangle.Y + Padding);
            ShortRectangle atlasRegion = new ShortRectangle(atlasX, atlasY, entry.region.Width, entry.region.Height);
            _storage.StoreSprite(entry.path, new SpriteAsset(atlasTexture, atlasRegion, entry.flip));
        }

        // Store animated sprites
        var animationFramesByPath = new Dictionary<string, ShortRectangle[]>();
        foreach (var kv in animatedSpriteInfos)
        {
            animationFramesByPath[kv.Key] = new ShortRectangle[kv.Value.frameIndices.Count];
        }

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (!entry.isAnimatedFrame || entry.animationPath == null)
            {
                continue;
            }

            int uniqueIndex = entryToUnique[i];
            int sortedIndex = originalToSorted[uniqueIndex];
            PackedRectangle packed = packedRectangles[sortedIndex];

            short atlasX = (short)(packed.Rectangle.X + Padding);
            short atlasY = (short)(packed.Rectangle.Y + Padding);
            ShortRectangle atlasRegion = new ShortRectangle(atlasX, atlasY, entry.region.Width, entry.region.Height);
            animationFramesByPath[entry.animationPath][entry.frameIndex] = atlasRegion;
        }

        foreach (var kv in animatedSpriteInfos)
        {
            string animationPath = kv.Key;
            (double frameDuration, SpriteFlip flip, _) = kv.Value;
            var frames = animationFramesByPath[animationPath];
            var immutableFrames = System.Collections.Immutable.ImmutableArray.CreateRange(frames);
            AnimatedSpriteAsset animatedSpriteAsset = new AnimatedSpriteAsset((float)frameDuration, atlasTexture, immutableFrames, Vector2.Zero, flip);
            _storage.StoreAnimatedSprite(animationPath, animatedSpriteAsset);
        }

        atlasImage.Dispose();
    }

    private void CollectSpritesRecursively(string directory,
        List<(string path, string texturePath, ShortRectangle region, SpriteFlip flip,
            bool isAnimatedFrame, string? animationPath, double frameDuration,
            int frameIndex, int totalFrames)> entries,
        Dictionary<string, (double frameDuration, SpriteFlip flip, List<int> frameIndices)> animatedSpriteInfos)
    {
        ReadOnlySpan<ContentFile> files = _contentSource.GetFiles(directory);
        foreach (ContentFile file in files)
        {
            if (file.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = file.Open();
                SpriteDto? spriteDto = null;
                AnimatedSpriteDto? animatedDto = null;
                try { spriteDto = JsonSerializer.Deserialize(stream, SpriteDtosJsonContext.Default.SpriteDto); } catch { }
                if (spriteDto != null)
                {
                    entries.Add((file.Path, spriteDto.Texture, spriteDto.TextureRegion, spriteDto.Flip,
                        false, null, 0, 0, 0));
                    continue;
                }
                stream.Position = 0;
                try { animatedDto = JsonSerializer.Deserialize(stream, SpriteDtosJsonContext.Default.AnimatedSpriteDto); } catch { }
                if (animatedDto != null)
                {
                    int totalFrames = animatedDto.Frames.Length;
                    var frameIndices = new List<int>(totalFrames);
                    for (int i = 0; i < totalFrames; i++)
                    {
                        entries.Add((file.Path, animatedDto.Texture, animatedDto.Frames[i], animatedDto.Flip,
                            true, file.Path, animatedDto.FrameDuration, i, totalFrames));
                        frameIndices.Add(entries.Count - 1);
                    }
                    animatedSpriteInfos[file.Path] = (animatedDto.FrameDuration, animatedDto.Flip, frameIndices);
                }
            }
        }
        ReadOnlySpan<string> subdirectories = _contentSource.GetDirectories(directory);
        foreach (string subdirectory in subdirectories)
        {
            CollectSpritesRecursively(subdirectory, entries, animatedSpriteInfos);
        }
    }

    private (ShortSize atlasSize, List<PackedRectangle> packedRectangles) PackImagesIntoAtlas(
        List<(Image image, ShortRectangle region)> images)
    {
        List<PackedRectangle> packedRectangles = new List<PackedRectangle>(images.Count);

        int atlasWidth = 1024;
        int atlasHeight = 1024;
        List<ShortRectangle> freeRectangles = new List<ShortRectangle>
        {
            new ShortRectangle(0, 0, (ushort)atlasWidth, (ushort)atlasHeight)
        };

        for (int i = 0; i < images.Count; i++)
        {
            ShortRectangle region = images[i].region;
            ushort width = (ushort)(region.Width + Padding * 2);
            ushort height = (ushort)(region.Height + Padding * 2);

            ShortRectangle? bestRect = FindBestFitRectangle(freeRectangles, width, height);

            if (bestRect == null)
            {
                atlasWidth *= 2;
                atlasHeight *= 2;

                freeRectangles.Clear();
                freeRectangles.Add(new ShortRectangle(0, 0, (ushort)atlasWidth, (ushort)atlasHeight));
                packedRectangles.Clear();
                i = -1;
                continue;
            }

            ShortRectangle usedRect = new ShortRectangle(bestRect.Value.X, bestRect.Value.Y, width, height);
            packedRectangles.Add(new PackedRectangle(usedRect, i));

            freeRectangles.Remove(bestRect.Value);

            SplitRectangle(freeRectangles, bestRect.Value, usedRect);
        }

        return (new ShortSize((ushort)atlasWidth, (ushort)atlasHeight), packedRectangles);
    }

    private ShortRectangle? FindBestFitRectangle(List<ShortRectangle> freeRectangles, ushort width, ushort height)
    {
        ShortRectangle? bestRect = null;
        int bestShortSide = int.MaxValue;

        foreach (ShortRectangle rect in freeRectangles)
        {
            if (rect.Width >= width && rect.Height >= height)
            {
                int leftoverHoriz = rect.Width - width;
                int leftoverVert = rect.Height - height;
                int shortSide = Math.Min(leftoverHoriz, leftoverVert);

                if (shortSide < bestShortSide)
                {
                    bestRect = rect;
                    bestShortSide = shortSide;
                }
            }
        }

        return bestRect;
    }

    private void SplitRectangle(List<ShortRectangle> freeRectangles, ShortRectangle freeRect, ShortRectangle usedRect)
    {
        int rightWidth = freeRect.X + freeRect.Width - (usedRect.X + usedRect.Width);
        int bottomHeight = freeRect.Y + freeRect.Height - (usedRect.Y + usedRect.Height);

        bool hasRight = rightWidth > 0;
        bool hasBottom = bottomHeight > 0;

        if (hasRight && hasBottom)
        {
            // Guillotine cut: give the larger remainder the full extent
            if (rightWidth > bottomHeight)
            {
                // Right gets full height, bottom gets only used width
                freeRectangles.Add(new ShortRectangle(
                    (short)(usedRect.X + usedRect.Width), freeRect.Y,
                    (ushort)rightWidth, freeRect.Height));
                freeRectangles.Add(new ShortRectangle(
                    freeRect.X, (short)(usedRect.Y + usedRect.Height),
                    usedRect.Width, (ushort)bottomHeight));
            }
            else
            {
                // Bottom gets full width, right gets only used height
                freeRectangles.Add(new ShortRectangle(
                    freeRect.X, (short)(usedRect.Y + usedRect.Height),
                    freeRect.Width, (ushort)bottomHeight));
                freeRectangles.Add(new ShortRectangle(
                    (short)(usedRect.X + usedRect.Width), freeRect.Y,
                    (ushort)rightWidth, usedRect.Height));
            }
        }
        else if (hasRight)
        {
            freeRectangles.Add(new ShortRectangle(
                (short)(usedRect.X + usedRect.Width), freeRect.Y,
                (ushort)rightWidth, freeRect.Height));
        }
        else if (hasBottom)
        {
            freeRectangles.Add(new ShortRectangle(
                freeRect.X, (short)(usedRect.Y + usedRect.Height),
                freeRect.Width, (ushort)bottomHeight));
        }
    }

    private RawImage CreateAtlasImage(
        List<(Image image, ShortRectangle region)> images,
        List<PackedRectangle> packedRectangles,
        ShortSize atlasSize)
    {
        int totalBytes = atlasSize.Width * atlasSize.Height * 4;
        byte[] atlasData = new byte[totalBytes];

        for (int i = 0; i < packedRectangles.Count; i++)
        {
            PackedRectangle packed = packedRectangles[i];
            (Image sourceImage, ShortRectangle region) = images[packed.ImageIndex];

            ReadOnlySpan<byte> sourceData = sourceImage.Data;
            ShortSize sourceSize = sourceImage.Size;

            for (int y = 0; y < region.Height; y++)
            {
                for (int x = 0; x < region.Width; x++)
                {
                    int sourceX = region.X + x;
                    int sourceY = region.Y + y;
                    int sourceIndex = (sourceY * sourceSize.Width + sourceX) * 4;

                    int atlasX = packed.Rectangle.X + Padding + x;
                    int atlasY = packed.Rectangle.Y + Padding + y;
                    int atlasIndex = (atlasY * atlasSize.Width + atlasX) * 4;

                    if (sourceIndex + 3 < sourceData.Length && atlasIndex + 3 < atlasData.Length)
                    {
                        atlasData[atlasIndex] = sourceData[sourceIndex];
                        atlasData[atlasIndex + 1] = sourceData[sourceIndex + 1];
                        atlasData[atlasIndex + 2] = sourceData[sourceIndex + 2];
                        atlasData[atlasIndex + 3] = sourceData[sourceIndex + 3];
                    }
                }
            }
        }

        // TODO: make sure pixel format is aligned
        return new RawImage(atlasData, atlasSize, PixelFormat.Rgba8888);
    }
}
