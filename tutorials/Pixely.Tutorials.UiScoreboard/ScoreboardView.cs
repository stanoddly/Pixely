using Pixely.Gpu;
using Pixely.Text;
using Pixely.Ui;

namespace Pixely.Tutorials.UiScoreboard;

/// <summary>
/// Builds its element tree exactly once and afterwards only writes to the elements it kept a
/// reference to. This is the difference from an immediate-mode UI, where every change re-runs the
/// whole build: here a changing label re-measures one element, and a changing bar width does not
/// re-measure anything at all.
/// </summary>
public sealed class ScoreboardView : UiView<ScoreboardViewModel>
{
    private static readonly Color Background = new(22, 25, 31, 255);
    private static readonly Color Panel = new(42, 50, 63, 255);
    private static readonly Color Accent = new(233, 138, 76, 255);
    private static readonly Color HealthBack = new(60, 32, 32, 255);
    private static readonly Color HealthFill = new(94, 191, 122, 255);
    private static readonly Color Caption = new(150, 162, 180, 255);
    private static readonly Color Value = new(236, 241, 247, 255);

    private const int BarWidth = 240;

    private readonly Font _font;
    private readonly Font _titleFont;

    private Label _score = null!;
    private Label _lives = null!;
    private Label _elapsed = null!;
    private Element _healthFill = null!;
    private Label _gameOver = null!;

    // Assignment only. The tree is built when the view is attached to a UiRoot.
    public ScoreboardView(Font font, Font titleFont, ScoreboardViewModel viewModel)
        : base(viewModel)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(titleFont);

        _font = font;
        _titleFont = titleFont;
    }

    protected override Element Build()
    {
        _score = new Label(_font) { Color = Value };
        _lives = new Label(_font) { Color = Value };
        _elapsed = new Label(_font) { Color = Value };

        _healthFill = new Column
        {
            Background = new SolidDrawable(HealthFill),
            Height = Sizing.Grow()
        };

        _gameOver = new Label(_titleFont, "GAME OVER")
        {
            Color = Accent,
            IsVisible = false
        };

        return new Column(gap: 20)
        {
            Background = new SolidDrawable(Background),
            Padding = new Thickness(28),
            Width = Sizing.Grow(),
            Height = Sizing.Grow(),
            Children =
            {
                new Label(_titleFont, "Scoreboard") { Color = Accent },

                new Column(gap: 10)
                {
                    Background = new SolidDrawable(Panel),
                    Padding = new Thickness(18),
                    Width = Sizing.Grow(),
                    Children =
                    {
                        StatRow("Score", _score),
                        StatRow("Lives", _lives),
                        StatRow("Time", _elapsed)
                    }
                },

                new Column(gap: 8)
                {
                    Children =
                    {
                        new Label(_font, "Health") { Color = Caption },

                        // A fixed-width track holding a fill whose width the view model drives.
                        // Changing that width re-arranges; it never re-measures.
                        new Column
                        {
                            Background = new SolidDrawable(HealthBack),
                            Width = Sizing.Fixed(BarWidth),
                            Height = Sizing.Fixed(18),
                            Children = { _healthFill }
                        }
                    }
                },

                _gameOver
            }
        };
    }

    protected override void Sync()
    {
        // Every assignment is a no-op when the value has not actually changed, so writing all of
        // them on any change costs nothing for the ones that stayed the same.
        _score.Content = ViewModel.Score.ToString();
        _lives.Content = ViewModel.Lives.ToString();
        _elapsed.Content = $"{ViewModel.Elapsed.TotalSeconds:0.0}s";

        _healthFill.Width = Sizing.Fixed((int)(BarWidth * ViewModel.HealthFraction));
        _gameOver.IsVisible = ViewModel.IsGameOver;
    }

    private Element StatRow(string caption, Label value)
    {
        return new Row(gap: 12)
        {
            Width = Sizing.Grow(),
            Children =
            {
                new Label(_font, caption) { Color = Caption, Width = Sizing.Fixed(90) },
                value
            }
        };
    }
}
