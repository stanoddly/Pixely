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
}
