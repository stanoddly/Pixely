using Pixely.Gpu;

namespace Pixely.Ui;

/// <summary>What a <see cref="Button"/> looks like, as one value a theme sets in one place.</summary>
/// <remarks>
/// A record struct rather than loose fields on <see cref="UiStyle"/>, so a button's look moves and
/// is overridden as a unit. Every member reads through a fallback, which is what keeps
/// <c>default</c> and <c>new()</c> from producing a style that paints nothing.
/// </remarks>
public readonly record struct ButtonAppearance
{
    /// <summary>The look a button has when no theme says otherwise, so one is visible without setup.</summary>
    public static StateDrawables DefaultBackground { get; } = new(new SolidDrawable(new Color(52, 60, 74, 255)))
    {
        Hovered = new SolidDrawable(new Color(70, 80, 98, 255)),
        Pressed = new SolidDrawable(new Color(38, 44, 55, 255)),
        Disabled = new SolidDrawable(new Color(40, 44, 51, 255))
    };

    /// <summary>
    /// Deliberately carries no hover tint: recolouring text on hover is a theme's decision, not
    /// something every button should start out doing.
    /// </summary>
    public static StateColors DefaultForeground { get; } = new(UiStyle.DefaultForeground)
    {
        Disabled = UiStyle.DefaultDisabledForeground
    };

    private readonly StateDrawables? _background;
    private readonly StateColors? _foreground;

    public StateDrawables Background
    {
        get => _background ?? DefaultBackground;
        init => _background = value;
    }

    /// <summary>What the text inside a button is drawn in — how a theme tints a label on hover.</summary>
    public StateColors Foreground
    {
        get => _foreground ?? DefaultForeground;
        init => _foreground = value;
    }
}

/// <summary>What a <see cref="TextBox"/> looks like.</summary>
public readonly record struct FieldAppearance
{
    /// <summary>Focused rather than hovered, because a field shows which one the typing goes to.</summary>
    public static StateDrawables DefaultBackground { get; } = new(new SolidDrawable(new Color(28, 32, 40, 255)))
    {
        Focused = new SolidDrawable(new Color(38, 44, 55, 255)),
        Disabled = new SolidDrawable(new Color(32, 34, 38, 255))
    };

    private readonly StateDrawables? _background;
    private readonly Color? _foreground;
    private readonly Color? _disabled;
    private readonly Color? _selection;

    public StateDrawables Background
    {
        get => _background ?? DefaultBackground;
        init => _background = value;
    }

    public Color Foreground
    {
        get => _foreground ?? UiStyle.DefaultForeground;
        init => _foreground = value;
    }

    /// <summary>
    /// Text in a field that cannot be used. Its own colour rather than a tint of the foreground,
    /// because how far to fade depends on what the text is drawn against.
    /// </summary>
    public Color Disabled
    {
        get => _disabled ?? UiStyle.DefaultDisabledForeground;
        init => _disabled = value;
    }

    /// <summary>
    /// Behind selected text. Its own value rather than part of a drawable, because it is painted
    /// under a run of characters whose extent is only known while the text is being drawn.
    /// </summary>
    public Color Selection
    {
        get => _selection ?? UiStyle.DefaultSelection;
        init => _selection = value;
    }

    /// <summary>The caret. Null means whatever the text is drawn in, which is what most themes want.</summary>
    public Color? Caret { get; init; }
}

/// <summary>
/// How much a run of text is meant to stand out. An intent rather than a colour, so a theme
/// decides what "secondary" looks like and every screen that says it gets the same answer.
/// </summary>
/// <remarks>
/// Separate from <see cref="TextRole"/>, which picks a font. Two small axes rather than one that
/// multiplies: a muted title is a real thing to want, and a role per combination is not.
/// </remarks>
public enum TextEmphasis
{
    Normal,
    Muted,
    Accent
}

/// <summary>What text outside a control of its own — a bare <see cref="Label"/> — is drawn in.</summary>
public readonly record struct TextAppearance
{
    private readonly Color? _foreground;
    private readonly Color? _muted;
    private readonly Color? _accent;
    private readonly Color? _disabled;

    public Color Foreground
    {
        get => _foreground ?? UiStyle.DefaultForeground;
        init => _foreground = value;
    }

    /// <summary>Captions and anything else deliberately quieter than the text around it.</summary>
    public Color Muted
    {
        get => _muted ?? UiStyle.DefaultMuted;
        init => _muted = value;
    }

    /// <summary>Headings and anything the screen is drawing the eye to.</summary>
    public Color Accent
    {
        get => _accent ?? UiStyle.DefaultAccent;
        init => _accent = value;
    }

    /// <inheritdoc cref="FieldAppearance.Disabled"/>
    public Color Disabled
    {
        get => _disabled ?? UiStyle.DefaultDisabledForeground;
        init => _disabled = value;
    }

    /// <summary>Disabled beats every emphasis: unusable is the more important thing to show.</summary>
    public Color Resolve(TextEmphasis emphasis, bool isEnabled)
    {
        if (!isEnabled)
        {
            return Disabled;
        }

        return emphasis switch
        {
            TextEmphasis.Muted => Muted,
            TextEmphasis.Accent => Accent,
            _ => Foreground
        };
    }
}
