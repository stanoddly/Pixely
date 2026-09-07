using Pixely.Gpu;

namespace Pixely.Ui;

/// <summary>
/// What a control looks like right now. Interaction states rather than widget names, so the same
/// vocabulary works for anything a pointer can press.
/// </summary>
public enum VisualState
{
    Normal,
    Hovered,
    Pressed,
    Disabled,

    /// <summary>
    /// Holding the keyboard. Only meaningful for something that takes typing, and last rather than
    /// beside the other interaction states so the values already in use keep the numbers they had.
    /// </summary>
    Focused
}

/// <summary>
/// One <see cref="Drawable"/> per <see cref="VisualState"/>. Unset states fall back to
/// <see cref="Normal"/>, so a flat look costs one drawable and a fully dressed one costs five.
/// Drawables rather than colours, because a hovered button is as likely to want a different
/// nine-patch as a different tint.
/// </summary>
public sealed class StateDrawables
{
    public StateDrawables(Drawable normal)
    {
        ArgumentNullException.ThrowIfNull(normal);
        Normal = normal;
    }

    public Drawable Normal { get; }

    public Drawable? Hovered { get; init; }

    public Drawable? Pressed { get; init; }

    public Drawable? Focused { get; init; }

    public Drawable? Disabled { get; init; }

    public Drawable Resolve(VisualState state)
    {
        return state switch
        {
            VisualState.Hovered => Hovered ?? Normal,
            VisualState.Pressed => Pressed ?? Normal,
            VisualState.Focused => Focused ?? Normal,
            VisualState.Disabled => Disabled ?? Normal,
            _ => Normal
        };
    }
}

/// <summary>
/// One <see cref="Color"/> per <see cref="VisualState"/>, with the fallbacks
/// <see cref="StateDrawables"/> has. A parallel type rather than a generic one over both, because
/// a drawable and a colour are the only two things that vary per state, and generalising would
/// rename what consumers already write.
/// </summary>
public sealed class StateColors
{
    public StateColors(Color normal)
    {
        Normal = normal;
    }

    public Color Normal { get; }

    public Color? Hovered { get; init; }

    public Color? Pressed { get; init; }

    public Color? Focused { get; init; }

    public Color? Disabled { get; init; }

    public Color Resolve(VisualState state)
    {
        return state switch
        {
            VisualState.Hovered => Hovered ?? Normal,
            VisualState.Pressed => Pressed ?? Normal,
            VisualState.Focused => Focused ?? Normal,
            VisualState.Disabled => Disabled ?? Normal,
            _ => Normal
        };
    }
}
