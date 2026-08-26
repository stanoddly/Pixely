using Pixely.Gpu;

namespace Pixely.Pencuil;

public record GuiStyle(
    short TextPadding, Color ActiveColor, Color InactiveColor, Color Background, short BorderThickness, ushort TextSize, Color TextColor, Color ActiveTextColor, Color SelectionColor
);

public readonly record struct ButtonStyle(
    Color BackgroundColor,
    Color HoverBackgroundColor,
    Color DisabledBackgroundColor,
    Color TextColor,
    Color HoverTextColor,
    Color DisabledTextColor,
    Color BorderColor,
    int BorderThickness)
{
    public ButtonStyle(Color backgroundColor, Color hoverBackgroundColor, Color textColor)
        : this(backgroundColor, hoverBackgroundColor, backgroundColor, textColor, textColor, textColor, Colors.Transparent, 0)
    {
    }

    internal ButtonStyle(GuiStyle style)
        : this(style.Background, style.ActiveColor, style.Background, style.TextColor, style.ActiveTextColor, style.InactiveColor, style.InactiveColor, style.BorderThickness)
    {
    }
}

public static class GuiStyles
{
    public static GuiStyle Style { get; } = new GuiStyle(5, new Color(120, 102, 49, 255), new Color(85, 77, 45, 255), new Color(24, 27, 24, 255), 2, 16, Colors.White, new Color(239, 139, 79, 255), new Color(51, 102, 170, 180));
}
