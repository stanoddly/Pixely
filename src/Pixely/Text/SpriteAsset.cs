using Pixely.Gpu;
using Pixely.Sprites;

namespace Pixely.Text;

public record TextSpriteAsset(Texture Texture, ShortRectangle ImageRegion) : SpriteAsset(Texture, ImageRegion);
