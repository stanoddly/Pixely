using Pixely.Gpu;

namespace Pixely.Pencuil;

public record GuiStyle(
    short TextPadding, Color ActiveColor, Color InactiveColor, Color Background, short BorderThickness, ushort TextSize, Color TextColor, Color ActiveTextColor, Color SelectionColor
);

public static class GuiStyles
{
    public static GuiStyle Style { get; } = new GuiStyle(5, new Color(120, 102, 49, 255), new Color(85, 77, 45, 255), new Color(24, 27, 24, 255), 2, 16, Colors.White, new Color(239, 139, 79, 255), new Color(51, 102, 170, 180));
}
