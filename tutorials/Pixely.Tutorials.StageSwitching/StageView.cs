using Pixely.Gpu;
using Pixely.Ui;

namespace Pixely.Tutorials.StageSwitching;

/// <summary>
/// Owned by a stage. Disposing the stage disposes this view, and the container's callbacks take it
/// off the root at the same time, which is why loading another stage swaps the panel.
/// </summary>
public sealed class StageView : UiView, IDisposable
{
    private readonly string _name;
    private readonly Color _color;

    public StageView(string name, Color color)
    {
        _name = name;
        _color = color;
        Console.WriteLine($"StageView created: {_name}");
    }

    public void Dispose()
    {
        Console.WriteLine($"StageView disposed: {_name}");
    }

    protected override Element BuildRoot()
    {
        return new Overlay
        {
            Children =
            {
                new Column
                {
                    Background = new SolidDrawable(_color),
                    Width = Sizing.Fixed(400),
                    Height = Sizing.Fixed(300),
                    HorizontalAlignment = Alignment.Center,
                    VerticalAlignment = Alignment.Center,
                    Offset = new Vector2Int(0, 40)
                }
            }
        };
    }

    protected override void Synchronize()
    {
    }
}
