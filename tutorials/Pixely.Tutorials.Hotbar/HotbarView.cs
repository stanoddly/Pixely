using Pixely.Content;
using Pixely.Gpu;
using Pixely.Sprites;
using Pixely.Ui;

namespace Pixely.Tutorials.Hotbar;

public sealed class HotbarView : UiView<HotbarViewModel>
{
    private const int SlotSize = 48;
    private const int SlotGap = 4;
    private const int BottomMargin = 16;
    private const int LabelGap = 4;

    private static readonly string[] SlotNames =
        ["Sword", "Shield", "Bow", "Potion", "Scroll", "Torch", "Ring", "Gem", "Key"];

    private static readonly string[] SlotIcons =
        ["sword", "shield", "bow", "potion", "scroll", "torch", "ring", "gem", "key"];

    private readonly HotbarSlot[] _slots;
    private readonly Label _label;

    public HotbarView(HotbarViewModel viewModel, ITextureLoader textureLoader)
        : base(viewModel)
    {
        _slots = new HotbarSlot[HotbarViewModel.SlotCount];
        for (int i = 0; i < _slots.Length; i++)
        {
            Texture texture = textureLoader.Load($"images/{SlotIcons[i]}.png");
            HotbarSlot slot = new(new SpriteAsset(texture, new ShortRectangle(0, 0, texture.Size.Width, texture.Size.Height)), SlotSize);

            int index = i;
            slot.Clicked += () => ViewModel.SelectedSlot = index;
            slot.HoverChanged += hovered => ShowLabel(hovered ? index : -1);
            _slots[i] = slot;
        }

        // Anchored above whichever slot is hovered, and hidden until one is. The label is not in the
        // view model: which slot the pointer is on is a fact about the tree, not about the game.
        // No font is passed: the label takes it from the root's UiStyle.
        _label = new Label { IsVisible = false };
    }

    protected override Element Build()
    {
        Row bar = new(gap: SlotGap)
        {
            HorizontalAlignment = Alignment.Center,
            VerticalAlignment = Alignment.End,
            Margin = new Thickness(0, 0, 0, BottomMargin)
        };

        foreach (HotbarSlot slot in _slots)
        {
            bar.Children.Add(slot);
        }

        // Two layers in one tree: the bar is placed by its alignment, the label by its anchor. An
        // overlay offers both the whole viewport, so neither knows about the other.
        return new Overlay
        {
            Children =
            {
                bar,
                new Element { Layout = new AnchoredLayout(Alignment.Center, Alignment.End), Width = Sizing.Grow(), Height = Sizing.Grow(), Children = { _label } }
            }
        };
    }

    protected override void Sync()
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i].IsSelected = i == ViewModel.SelectedSlot;
        }
    }

    private void ShowLabel(int slotIndex)
    {
        if (slotIndex < 0)
        {
            _label.IsVisible = false;
            return;
        }

        // Bounds are absolute and valid here: hover is raised during the build, after arrange.
        Rectangle bounds = _slots[slotIndex].Bounds;
        _label.Content = SlotNames[slotIndex];
        _label.Anchor = new Vector2Int(bounds.X + bounds.Width / 2, bounds.Y - LabelGap);
        _label.IsVisible = true;
    }
}
