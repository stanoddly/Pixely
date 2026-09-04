using Pixely.App;
using Pixely.Input;
using Pixely.RenderOrchestration;
using Pixely.Text;
using Pixely.Ui;

namespace Pixely.Tutorials.UiScoreboard;

/// <summary>
/// A retained UI driven by a view model. The element tree is built once; afterwards the view only
/// assigns to the elements it kept a reference to, and nothing is rebuilt — not when a label's text
/// changes every frame, not when a bar resizes, not when an element appears.
/// </summary>
static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder builder = new();
        builder
            .ConfigureContent(contentSourceBuilder => contentSourceBuilder.AddProjectDirectory("../Pixely.Tutorials.Hotbar/Content"))
            .UseDefaultRendering(new WindowConfig(Size: (640, 520), Title: "Pixely.Ui — Scoreboard"));

        builder.UseUi();
        builder.AddSingleton(new ScoreboardViewModel());
        builder.AddSingleton<IUpdatable, ScoreboardSimulation>(provider =>
            new ScoreboardSimulation(
                provider.GetRequiredService<ScoreboardViewModel>(),
                provider.GetRequiredService<FrameContext>()));

        builder.OnStart((
            IKeyboardService keyboardService,
            AppControl appControl,
            ScoreboardViewModel viewModel) =>
        {
            Console.WriteLine("Health drains on its own. Space: score   H: damage   L: lose a life   R: reset   Escape: quit");

            keyboardService.KeyDown += eventArgs =>
            {
                switch (eventArgs.Key)
                {
                    // Repeats are delivered now, so holding H keeps damaging while the discrete
                    // actions below stay on the initial press.
                    case VirtualKey.H:
                        viewModel.Damage(2);
                        break;

                    case VirtualKey.Space when !eventArgs.Repeat:
                        viewModel.Score += 10;
                        break;

                    case VirtualKey.L when !eventArgs.Repeat:
                        viewModel.Lives = Math.Max(0, viewModel.Lives - 1);
                        break;

                    case VirtualKey.R when !eventArgs.Repeat:
                        viewModel.Reset();
                        break;

                    case VirtualKey.Escape:
                        appControl.Quit();
                        break;
                }
            };
        });

        using IPixelyApp pixelyApp = builder.Build();

        IFontSystem fontSystem = pixelyApp.ServiceProvider.GetRequiredService<IFontSystem>();

        ScoreboardView view = new(
            fontSystem.Load("fonts/GohuFont-Medium.ttf", 16),
            fontSystem.Load("fonts/GohuFont-Medium.ttf", 20),
            pixelyApp.ServiceProvider.GetRequiredService<ScoreboardViewModel>());

        pixelyApp.ServiceProvider.GetUiRoot().AddView(view);

        return pixelyApp.Run();
    }
}

/// <summary>
/// Drives the view model over time, so the demo shows the mechanism without anyone pressing a key.
/// The clock changes a label every frame, the draining health changes an element's width, and
/// running out of lives makes an element appear. Three different kinds of update, none of which
/// rebuilds the tree.
/// </summary>
internal sealed class ScoreboardSimulation : IUpdatable
{
    private const float HealthDrainPerSecond = 6f;
    private const int ScorePerSecondSurvived = 5;

    private readonly ScoreboardViewModel _viewModel;
    private readonly FrameContext _frameContext;

    private float _healthRemainder;
    private float _scoreRemainder;

    internal ScoreboardSimulation(ScoreboardViewModel viewModel, FrameContext frameContext)
    {
        _viewModel = viewModel;
        _frameContext = frameContext;
    }

    public void Update()
    {
        if (_viewModel.IsGameOver)
        {
            return;
        }

        float delta = _frameContext.TimeDelta;
        _viewModel.Elapsed += TimeSpan.FromSeconds(delta);

        _healthRemainder += HealthDrainPerSecond * delta;
        int damage = (int)_healthRemainder;
        if (damage > 0)
        {
            _healthRemainder -= damage;
            _viewModel.Damage(damage);
        }

        _scoreRemainder += ScorePerSecondSurvived * delta;
        int points = (int)_scoreRemainder;
        if (points > 0)
        {
            _scoreRemainder -= points;
            _viewModel.Score += points;
        }
    }
}
