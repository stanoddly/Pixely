using Pixely.Text;

namespace Pixely.Ui;

/// <summary>
/// Defaults shared by every element in a <see cref="UiRoot"/>, so a view does not have to carry
/// fonts through its constructors or restate the same values in each tree it builds.
/// </summary>
/// <remarks>
/// Fonts have to be loaded from content, so there is no built-in default: an application supplies
/// its own style. Roles rather than a single font, because "the same face at two sizes" is a type
/// scale, and every real screen needs at least a title and body distinction.
/// </remarks>
public sealed class UiStyle
{
    public UiStyle(Font body)
    {
        ArgumentNullException.ThrowIfNull(body);

        Body = body;
        Title = body;
        Small = body;
    }

    /// <summary>The font a <see cref="Label"/> uses when it is not given one.</summary>
    public Font Body { get; }

    /// <summary>Defaults to <see cref="Body"/>.</summary>
    public Font Title { get; init; }

    /// <summary>Defaults to <see cref="Body"/>.</summary>
    public Font Small { get; init; }
}
