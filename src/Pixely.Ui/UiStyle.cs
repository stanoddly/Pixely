using Pixely.Gpu;
using Pixely.Text;

namespace Pixely.Ui;

/// <summary>
/// The look of every element under a <see cref="UiRoot"/>. Authoritative: elements hold no colours
/// or drawables of their own, so a theme is what a screen looks like rather than a starting point
/// that individual controls quietly walk away from.
/// </summary>
/// <remarks>
/// <para>
/// A record, so a variation is <c>style with { Button = quiet }</c> rather than a restatement of
/// everything that did not change. Appearances are grouped per control, which keeps a variation to
/// the one member that differs and keeps this type from growing a field per control per property.
/// </para>
/// <para>
/// Fonts have to be loaded from content, so they have no built-in default and stay optional: a
/// style that only themes buttons needs none. Roles rather than a single font, because "the same
/// face at two sizes" is a type scale, and every real screen needs at least a title and body
/// distinction.
/// </para>
/// </remarks>
public sealed record UiStyle
{
    public UiStyle()
    {
    }

    /// <summary>Puts <paramref name="body"/> in every text role, which is where most screens start.</summary>
    public UiStyle(IFont body)
    {
        ArgumentNullException.ThrowIfNull(body);

        Body = body;
        Title = body;
        Small = body;
    }

    /// <summary>What a root paints with until it is given a style, so nothing has to check for null.</summary>
    public static UiStyle Default { get; } = new();

    /// <summary>The font a <see cref="Label"/> uses when it is not given one.</summary>
    public IFont? Body { get; init; }

    /// <summary>Defaults to <see cref="Body"/>.</summary>
    public IFont? Title { get; init; }

    /// <summary>Defaults to <see cref="Body"/>.</summary>
    public IFont? Small { get; init; }

    public ButtonAppearance Button { get; init; }

    public FieldAppearance Field { get; init; }

    /// <summary>Text that is not inside a control with a look of its own.</summary>
    public TextAppearance Text { get; init; }

    /// <summary>Used when no style supplies one, so a field is usable without any setup.</summary>
    public static Color DefaultSelection { get; } = new(51, 102, 170, 180);

    /// <inheritdoc cref="DefaultSelection"/>
    public static Color DefaultForeground { get; } = Colors.White;

    /// <inheritdoc cref="DefaultSelection"/>
    public static Color DefaultDisabledForeground { get; } = new(130, 130, 130, 255);

    /// <inheritdoc cref="DefaultSelection"/>
    public static Color DefaultMuted { get; } = new(160, 168, 180, 255);

    /// <inheritdoc cref="DefaultSelection"/>
    public static Color DefaultAccent { get; } = new(239, 139, 79, 255);
}
