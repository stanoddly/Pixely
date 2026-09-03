using Pixely.Gpu;
using Pixely.Pencuil;
using Pixely.Text;

namespace Pixely.Tutorials.ScrollList;

public class ScrollListViewModel : IPencuilViewModel
{
    public bool IsDirty { get; set; } = true;

    private int _selectedItem = -1;

    public int ScrollOffset;
    public int LogScrollOffset;

    public int SelectedItem
    {
        get
        {
            return _selectedItem;
        }
        set
        {
            if (_selectedItem != value)
            {
                _selectedItem = value;
                IsDirty = true;
            }
        }
    }
}

public class ScrollListView : PencuilViewBase<ScrollListViewModel>
{
    private const int ItemCount = 60;
    private const int ItemHeight = 32;
    private const int ItemGap = 2;
    private const int ListWidth = 320;
    private const int ListHeight = 420;
    private const int LogWidth = 380;
    private const int LogHeight = 140;
    private const int LineHeight = 18;

    private static readonly Color BackgroundColor = new(28, 30, 34, 255);
    private static readonly Color PanelColor = new(38, 41, 46, 255);
    private static readonly Color TextColor = new(235, 238, 242, 255);
    private static readonly Color SelectedColor = new(239, 139, 79, 255);

    private static readonly string[] LogLines =
    [
        "The horizontal view below scrolls a single long line.",
        "Wheel over either panel to scroll it.",
        "Drag the thumb, or click the bare track to page.",
        "Content clipped out of view stops responding to clicks.",
        "Rows keep their hover highlight while they are visible.",
        "The scrollbar is drawn after the content, so it stays on top.",
        "Offsets live in the view model, like every other Pencuil value.",
    ];

    private readonly Font _font;

    public ScrollListView(ScrollListViewModel viewModel, IFontSystem fontSystem)
        : base(viewModel)
    {
        _font = fontSystem.Load("fonts/GohuFont-Medium.ttf", 14);
    }

    public override void Build(Pencil pencil)
    {
        pencil.MoveTo(0, 0);
        pencil.Rectangle(pencil.BottomRight.X, pencil.BottomRight.Y, BackgroundColor);

        int startX = pencil.Center.X - (ListWidth + 40 + LogWidth) / 2;
        int startY = pencil.Center.Y - ListHeight / 2;

        BuildItemList(pencil, startX, startY);
        BuildSelection(pencil, startX + ListWidth + 40, startY);
        BuildHorizontalLog(pencil, startX + ListWidth + 40, startY + ListHeight - LogHeight);
    }

    private void BuildItemList(Pencil pencil, int x, int y)
    {
        int contentExtent = ItemCount * (ItemHeight + ItemGap) - ItemGap;

        pencil.MoveTo(x, y);
        pencil.Rectangle(ListWidth, ListHeight, PanelColor);

        pencil.MoveTo(x, y);
        using (pencil.WithGap(ItemGap))
        using (pencil.ScrollView(id: 1, ListWidth, ListHeight, ref ViewModel.ScrollOffset, contentExtent))
        {
            for (int index = 0; index < ItemCount; index++)
            {
                BuildItem(pencil, index);
            }
        }
    }

    private void BuildItem(Pencil pencil, int index)
    {
        int itemWidth = ListWidth - pencil.Style.ScrollBarThickness;
        bool selected = ViewModel.SelectedItem == index;
        Vector2Int position = pencil.CurrentPosition;

        if (pencil.ClickArea(new Rectangle(position, new Vector2Int(itemWidth, ItemHeight))))
        {
            ViewModel.SelectedItem = index;
        }

        Color itemColor = selected ? SelectedColor : pencil.Style.Background;
        pencil.HoverRectangle(itemWidth, ItemHeight, itemColor, pencil.Style.ActiveColor);

        Vector2Int savedPosition = pencil.CurrentPosition;
        pencil.MoveTo(position.X + 8, position.Y + 8);
        pencil.Text($"Item {index + 1}", _font, TextColor);
        pencil.CurrentPosition = savedPosition;
    }

    private void BuildSelection(Pencil pencil, int x, int y)
    {
        string text = ViewModel.SelectedItem < 0
            ? "Nothing selected"
            : $"Selected item {ViewModel.SelectedItem + 1}";

        pencil.MoveTo(x, y);
        pencil.Text(text, _font, TextColor);
    }

    private void BuildHorizontalLog(Pencil pencil, int x, int y)
    {
        int widestLine = 0;
        foreach (string line in LogLines)
        {
            widestLine = Math.Max(widestLine, pencil.MeasureText(line, _font).X);
        }

        pencil.MoveTo(x, y);
        pencil.Rectangle(LogWidth, LogHeight, PanelColor);

        pencil.MoveTo(x, y);
        using (pencil.ScrollView(id: 2, LogWidth, LogHeight, ref ViewModel.LogScrollOffset, widestLine + 16, Orientation.Horizontal))
        {
            Vector2Int origin = pencil.CurrentPosition;
            for (int index = 0; index < LogLines.Length; index++)
            {
                pencil.MoveTo(origin.X + 8, origin.Y + 8 + index * LineHeight);
                pencil.Text(LogLines[index], _font, TextColor);
            }
        }
    }
}
