# Sprites

`RegisterSpriteLoading()` registers `ISpriteAssetLoader` and `IAnimatedSpriteAssetLoader`. Both load a JSON file from the `ContentSource` by path, resolve its texture through `ITextureLoader`, and cache the asset in `SpriteAssetStorage`, so loading the same path twice returns the same instance.

## Sprite file

```json
{"texture": "hero.png", "textureRegion": [0, 0, 32, 32], "anchorOffset": [16, 32], "flip": "Horizontal"}
```

| Field | Type | Meaning |
| --- | --- | --- |
| `texture` | string, required | Texture path loaded through `ITextureLoader` |
| `textureRegion` | `[x, y, width, height]`, required | The region of the texture the sprite shows; `x` and `y` are `short`, `width` and `height` are `ushort` |
| `anchorOffset` | `[x, y]` | `SpriteAsset.AnchorOffset`, defaults to `[0, 0]` |
| `flip` | string | `None` (default), `Horizontal`, `Vertical` or `Both` |

## Animated sprite file

```json
{
  "frameDuration": 0.1,
  "texture": "hero.png",
  "frames": [[0, 0, 32, 32], [32, 0, 32, 32], [64, 0, 32, 32]],
  "anchorOffset": [16, 32],
  "flip": "None"
}
```

`frameDuration` is seconds per frame and `frames` is an array of `textureRegion` values, played in order. `texture`, `anchorOffset` and `flip` mean the same as for a sprite and `flip` applies to every frame.

Property names are case-insensitive and `//` comments are allowed.

## Mirroring

Region dimensions are always positive; mirroring is the separate `flip` field, which maps to the `SpriteFlip` flags enum and is applied when the asset computes its UVs (`SpriteAsset.CalculateTextureRegionUVs()`, `AnimatedSpriteAsset.CalculateTextureRegionUVs(frameIndex)`, both calling `Texture.CalculateTextureRegionUVs(region, flip)`).

```csharp
SpriteAsset facingLeft = new SpriteAsset(texture, new ShortRectangle(0, 0, 32, 32), SpriteFlip.Horizontal);
```
