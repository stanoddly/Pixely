using Pixely.App;
using Pixely.Gpu;
using Pixely.Ui;

namespace Pixely.Tutorials.StageSwitching;

/// <summary>
/// Lives in the root provider and outlasts every stage. Each button loads a stage that registers
/// its own view: the stage's provider inherits the root's callbacks, so the view is added to the
/// root's <see cref="UiRoot"/> when the stage is built and taken off it when the stage is disposed.
/// </summary>
public sealed class MenuView : UiView<MenuViewModel>
{
    private static readonly Color BackgroundColor = new(28, 30, 34, 255);
    private static readonly Drawable ActiveBackground = new SolidDrawable(new Color(46, 139, 87, 255));

    private const int ButtonWidth = 160;
    private const int ButtonHeight = 44;
    private const int ButtonGap = 16;
    private const int TopMargin = 24;

    private readonly Button _stageA;
    private readonly Button _stageB;

    public MenuView(MenuViewModel viewModel, IStageManager stageManager)
        : base(viewModel)
    {
        _stageA = StageButton("Stage A", stageManager, "A", new Color(70, 130, 180, 255));
        _stageB = StageButton("Stage B", stageManager, "B", new Color(180, 100, 70, 255));
    }

    protected override Element Build()
    {
        return new Overlay
        {
            Background = new SolidDrawable(BackgroundColor),
            Children =
            {
                new Row(gap: ButtonGap)
                {
                    HorizontalAlignment = Alignment.Center,
                    Margin = new Thickness(0, TopMargin, 0, 0),
                    Children = { _stageA, _stageB }
                }
            }
        };
    }

    protected override void Sync()
    {
        // A Button honours an assigned Background ahead of the style's, as one look for every
        // state. Clearing it hands the button back to the style, hover and all.
        _stageA.Background = ViewModel.ActiveStage == "A" ? ActiveBackground : null;
        _stageB.Background = ViewModel.ActiveStage == "B" ? ActiveBackground : null;
    }

    private Button StageButton(string text, IStageManager stageManager, string stage, Color color)
    {
        Button button = new(text) { Width = Sizing.Fixed(ButtonWidth), Height = Sizing.Fixed(ButtonHeight) };
        button.Clicked += () =>
        {
            ViewModel.ActiveStage = stage;
            stageManager.Load(services => services.AddSingleton<IUiView>(new StageView($"Stage {stage}", color)));
        };
        return button;
    }
}
