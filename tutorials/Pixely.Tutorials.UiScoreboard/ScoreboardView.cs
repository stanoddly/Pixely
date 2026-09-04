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
public sealed class ScoreboardView
{
    private static readonly Color Background = new(22, 25, 31, 255);
    private static readonly Color Panel = new(42, 50, 63, 255);
    private static readonly Color Accent = new(233, 138, 76, 255);
    private static readonly Color HealthBack = new(60, 32, 32, 255);
    private static readonly Color HealthFill = new(94, 191, 122, 255);
    private static readonly Color Caption = new(150, 162, 180, 255);
    private static readonly Color Value = new(236, 241, 247, 255);

    private const int BarWidth = 240;

    private readonly ScoreboardViewModel _viewModel;

    private readonly Label _score;
    private readonly Label _lives;
    private readonly Label _elapsed;
    private readonly Element _healthFill;
    private readonly Label _gameOver;

    public ScoreboardView(Font font, Font titleFont, ScoreboardViewModel viewModel)
    {
        _viewModel = viewModel;

        _score = new Label(font) { Color = Value };
        _lives = new Label(font) { Color = Value };
        _elapsed = new Label(font) { Color = Value };

        _healthFill = new Column
        {
            Background = new SolidDrawable(HealthFill),
            Height = Sizing.Grow()
        };

        _gameOver = new Label(titleFont, "GAME OVER")
        {
            Color = Accent,
            IsVisible = false,
            HorizontalAlignment = Alignment.Center
        };

        Root = Build(titleFont);

        // The view model is the only thing that says "something changed"; nothing polls the tree.
        viewModel.Changed += Sync;
        Sync();
    }

    public Element Root { get; }

    private Element Build(Font titleFont)
    {
        return new Column(gap: 20)
        {
            Background = new SolidDrawable(Background),
            Padding = new Thickness(28),
            Width = Sizing.Grow(),
            Height = Sizing.Grow(),
            Children =
            {
                new Label(titleFont, "Scoreboard") { Color = Accent },

                new Column(gap: 10)
                {
                    Background = new SolidDrawable(Panel),
                    Padding = new Thickness(18),
                    Width = Sizing.Grow(),
                    Children =
                    {
                        StatRow("Score", _score, titleFont),
                        StatRow("Lives", _lives, titleFont),
                        StatRow("Time", _elapsed, titleFont)
                    }
                },

                new Column(gap: 8)
                {
                    Children =
                    {
                        new Label(titleFont, "Health") { Color = Caption },

                        // The bar is a fixed-width track with a fill whose width the view model
                        // drives. Changing it re-arranges; it never re-measures.
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

    private static Element StatRow(string caption, Label value, Font font)
    {
        return new Row(gap: 12)
        {
            Width = Sizing.Grow(),
            Children =
            {
                new Label(font, caption) { Color = Caption, Width = Sizing.Fixed(90) },
                value
            }
        };
    }

    private void Sync()
    {
        // Every assignment is a no-op when the value has not actually changed, so writing all of
        // them on any change costs nothing for the ones that stayed the same.
        _score.Content = _viewModel.Score.ToString();
        _lives.Content = _viewModel.Lives.ToString();
        _elapsed.Content = $"{_viewModel.Elapsed.TotalSeconds:0.0}s";

        _healthFill.Width = Sizing.Fixed((int)(BarWidth * _viewModel.HealthFraction));
        _gameOver.IsVisible = _viewModel.IsGameOver;
    }
}
