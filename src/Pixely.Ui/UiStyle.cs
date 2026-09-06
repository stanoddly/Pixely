using Pixely.Gpu;
using Pixely.Text;

namespace Pixely.Ui;

/// <summary>
/// Defaults shared by every element in a <see cref="UiRoot"/>, so a view does not have to carry
/// fonts through its constructors or restate the same values in each tree it builds.
/// </summary>
/// <remarks>
/// Fonts have to be loaded from content, so they have no built-in default and stay optional: a
/// style that only themes buttons needs none. Roles rather than a single font, because "the same
/// face at two sizes" is a type scale, and every real screen needs at least a title and body
/// distinction.
/// </remarks>
public sealed class UiStyle
{
    public UiStyle()
    {
    }

    /// <summary>Puts <paramref name="body"/> in every text role, which is where most screens start.</summary>
    public UiStyle(Font body)
    {
        ArgumentNullException.ThrowIfNull(body);

        Body = body;
        Title = body;
        Small = body;
    }

    /// <summary>The font a <see cref="Label"/> uses when it is not given one.</summary>
    public Font? Body { get; init; }

    /// <summary>Defaults to <see cref="Body"/>.</summary>
    public Font? Title { get; init; }

    /// <summary>Defaults to <see cref="Body"/>.</summary>
    public Font? Small { get; init; }

    /// <summary>
    /// What a <see cref="Button"/> paints when it was not given its own. Unlike a font this has a
    /// usable default, so a screen full of buttons needs no style at all and a themed one restates
    /// nothing per button.
    /// </summary>
    public StateDrawables ButtonBackground { get; init; } = Button.DefaultBackground;

    /// <summary>
    /// Behind selected text. Its own value rather than part of a drawable, because it is painted
    /// under a run of characters whose extent is only known while the text is being drawn.
    /// </summary>
    public Color Selection { get; init; } = DefaultSelection;

    /// <summary>Text and anything drawn as text, when the element does not say otherwise.</summary>
    public Color Foreground { get; init; } = DefaultForeground;

    /// <summary>
    /// Text in a control that cannot be used. Its own colour rather than a tint of the foreground,
    /// because how far to fade depends on what the text is drawn against.
    /// </summary>
    public Color DisabledForeground { get; init; } = DefaultDisabledForeground;

    /// <summary>Behind an editable field, per state — which is how a focused one shows that it is.</summary>
    public StateDrawables FieldBackground { get; init; } = TextBox.DefaultBackground;

    /// <summary>The caret in an editable field. Defaults to whatever the text is drawn in.</summary>
    public Color? Caret { get; init; }

    /// <summary>Used when no style supplies one, so a field is usable without any setup.</summary>
    public static Color DefaultSelection { get; } = new(51, 102, 170, 180);

    /// <inheritdoc cref="DefaultSelection"/>
    public static Color DefaultForeground { get; } = Colors.White;

    /// <inheritdoc cref="DefaultSelection"/>
    public static Color DefaultDisabledForeground { get; } = new(130, 130, 130, 255);
}
