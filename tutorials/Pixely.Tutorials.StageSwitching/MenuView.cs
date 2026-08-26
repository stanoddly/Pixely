using Pixely.App;
using Pixely.Gpu;
using Pixely.Pencuil;
using Pixely.Text;

namespace Pixely.Tutorials.StageSwitching;

public class MenuView : IPencuilView
{
    private static readonly Color BackgroundColor = new(28, 30, 34, 255);
    private static readonly Color ButtonColor = new(62, 87, 121, 255);
    private static readonly Color ButtonHoverColor = new(78, 112, 156, 255);
    private static readonly Color ActiveButtonColor = new(46, 139, 87, 255);
    private static readonly Color ActiveButtonHoverColor = new(60, 179, 113, 255);
    private static readonly Color TextColor = new(235, 238, 242, 255);

    private const int ButtonWidth = 160;
    private const int ButtonHeight = 44;
    private const int ButtonGap = 16;
    private const int TopMargin = 24;

    private readonly IStageManager _stageManager;
    private readonly Font _font;
    private string? _activeStage;
    private bool _dirty = true;


    public MenuView(IStageManager stageManager, IFontSystem fontSystem)
    {
        _stageManager = stageManager;
        _font = fontSystem.Load("fonts/GohuFont-Medium.ttf", 16);
    }

    public bool ConsumeDirty()
    {
        bool dirty = _dirty;
        _dirty = false;
        return dirty;
    }

    public void Build(Pencil pencil)
    {
        pencil.MoveTo(0, 0);
        pencil.Rectangle(pencil.BottomRight.X, pencil.BottomRight.Y, BackgroundColor);

        int totalWidth = ButtonWidth * 2 + ButtonGap;
        int startX = pencil.Center.X - totalWidth / 2;
        int y = TopMargin;

        if (DrawStageButton(pencil, startX, y, "Stage A", _activeStage == "A"))
        {
            _activeStage = "A";
            _dirty = true;
            _stageManager.Load(services =>
            {
                services.AddSingleton<IPencuilView>(new StageView("Stage A", new Color(70, 130, 180, 255)));
            });
        }

        if (DrawStageButton(pencil, startX + ButtonWidth + ButtonGap, y, "Stage B", _activeStage == "B"))
        {
            _activeStage = "B";
            _dirty = true;
            _stageManager.Load(services =>
            {
                services.AddSingleton<IPencuilView>(new StageView("Stage B", new Color(180, 100, 70, 255)));
            });
        }
    }

    private bool DrawStageButton(Pencil pencil, int x, int y, string text, bool active)
    {
        Rectangle area = new Rectangle(x, y, ButtonWidth, ButtonHeight);
        Color color = active ? ActiveButtonColor : ButtonColor;
        Color hoverColor = active ? ActiveButtonHoverColor : ButtonHoverColor;
        Color actualColor = area.Intersects(pencil.CursorPosition) ? hoverColor : color;

        pencil.MoveTo(x, y);
        pencil.Rectangle(ButtonWidth, ButtonHeight, actualColor);
        CursorState state = pencil.HitArea(area);

        Vector2Int textSize = pencil.MeasureText(text, _font);
        pencil.MoveTo(x + (ButtonWidth - textSize.X) / 2, y + (ButtonHeight - textSize.Y) / 2);
        pencil.Text(text, _font, TextColor);

        return state == CursorState.Clicked;
    }
}
