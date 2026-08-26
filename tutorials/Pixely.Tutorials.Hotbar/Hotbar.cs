using Pixely.Content;
using Pixely.Gpu;
using Pixely.Input;
using Pixely.Pencuil;
using Pixely.Sprites;
using Pixely.Text;

namespace Pixely.Tutorials.Hotbar;

public class HotbarViewModel : IPencuilViewModel
{
    public bool IsDirty { get; set; } = true;

    private int _selectedSlot;

    public int SelectedSlot
    {
        get => _selectedSlot;
        set
        {
            if (_selectedSlot != value)
            {
                _selectedSlot = value;
                IsDirty = true;
            }
        }
    }
}

public class Hotbar : PencuilView<HotbarViewModel>
{
    private const int SlotCount = 9;
    private const int SlotSize = 48;
    private const int SlotGap = 4;
    private const int LabelGap = 4;

    private static readonly Color SlotColor = new(60, 60, 60, 255);
    private static readonly Color SelectedColor = new(200, 200, 200, 255);
    private static readonly Color HoverColor = new(100, 100, 100, 255);

    private static readonly string[] SlotNames =
        ["Sword", "Shield", "Bow", "Potion", "Scroll", "Torch", "Ring", "Gem", "Key"];

    private static readonly string[] SlotIcons =
        ["sword", "shield", "bow", "potion", "scroll", "torch", "ring", "gem", "key"];

    private readonly Font _font;
    private readonly SpriteAsset[] _slotSprites;
    private int _hoveredSlot = -1;

    public Hotbar(HotbarViewModel viewModel, IKeyboardService keyboardService, IFontSystem fontSystem, ITextureLoader textureLoader)
        : base(viewModel)
    {
        _font = fontSystem.Load("fonts/GohuFont-Medium.ttf", 14);

        _slotSprites = new SpriteAsset[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            Texture texture = textureLoader.Load($"images/{SlotIcons[i]}.png");
            _slotSprites[i] = new SpriteAsset(texture, new ShortRectangle(0, 0, texture.Size.Width, texture.Size.Height));
        }

        keyboardService.SubscribeKeyDown(0, args =>
        {
            int index = args.Scancode - Scancode.Number1;
            if (index >= 0 && index < SlotCount)
            {
                ViewModel.SelectedSlot = index;
            }
        });
    }

    public override void Build(Pencil pencil)
    {
        int hoveredSlot = -1;
        Vector2Int hoveredPos = default;

        using (pencil.WithGap(SlotGap))
        using (pencil.WithDirection(LayoutDirection.Right))
        {
            int totalExtent = SlotCount * SlotSize + (SlotCount - 1) * SlotGap;
            Vector2Int anchor = pencil.BottomCenter;
            pencil.MoveTo(anchor.X - totalExtent / 2, anchor.Y - SlotSize - 16);

            for (int i = 0; i < SlotCount; i++)
            {
                Vector2Int slotPos = pencil.CurrentPosition;

                Color color = i == ViewModel.SelectedSlot ? SelectedColor
                    : i == _hoveredSlot ? HoverColor
                    : SlotColor;

                pencil.Rectangle(SlotSize, SlotSize, color);
                CursorState state = pencil.HitArea(new Rectangle(slotPos, new Vector2Int(SlotSize)));

                // Draw icon centered in slot (32x32 icon in 48x48 slot = 8px padding)
                Vector2Int nextPos = pencil.CurrentPosition;
                Vector2Int nextSize = pencil.CurrentSize;
                const int iconPadding = (SlotSize - 32) / 2;
                pencil.MoveTo(slotPos.X + iconPadding, slotPos.Y + iconPadding);
                pencil.Image(_slotSprites[i], Colors.White);
                pencil.CurrentPosition = nextPos;
                pencil.CurrentSize = nextSize;

                if (state == CursorState.Clicked)
                {
                    ViewModel.SelectedSlot = i;
                }
                if (state >= CursorState.Hovered)
                {
                    hoveredSlot = i;
                    hoveredPos = slotPos;
                }
            }
        }

        if (hoveredSlot >= 0)
        {
            string label = SlotNames[hoveredSlot];
            Vector2Int textSize = pencil.MeasureText(label, _font);
            pencil.MoveTo(
                hoveredPos.X + (SlotSize - textSize.X) / 2,
                hoveredPos.Y - textSize.Y - LabelGap);
            pencil.Text(label, _font, Colors.White);
        }

        _hoveredSlot = hoveredSlot;
    }
}
