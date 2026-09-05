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
    Disabled
}

/// <summary>
/// One <see cref="Drawable"/> per <see cref="VisualState"/>. Unset states fall back to
/// <see cref="Normal"/>, so a flat look costs one drawable and a fully dressed one costs four.
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

    public Drawable? Disabled { get; init; }

    public Drawable Resolve(VisualState state)
    {
        return state switch
        {
            VisualState.Hovered => Hovered ?? Normal,
            VisualState.Pressed => Pressed ?? Normal,
            VisualState.Disabled => Disabled ?? Normal,
            _ => Normal
        };
    }
}
