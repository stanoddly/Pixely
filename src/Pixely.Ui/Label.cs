using Pixely.Gpu;
using Pixely.Text;

namespace Pixely.Ui;

/// <summary>Which font of the <see cref="UiStyle"/> a label uses when it was not given one.</summary>
public enum TextRole
{
    Body,
    Title,
    Small
}

/// <summary>
/// A run of text. The rasterised sprite is kept until the content or font changes, so updating a
/// label costs a measure of one element rather than a rebuild of everything around it.
/// </summary>
/// <remarks>
/// Named Label rather than Text so that consumers can use <c>Pixely.Text</c> — which is where
/// <see cref="IFont"/> lives — without the two colliding.
/// </remarks>
public sealed class Label : Element
{
    private string _content;
    private IFont? _font;
    private TextRole _role = TextRole.Body;
    private Color _color = Colors.White;
    private TextSpriteAsset? _sprite;
    private IFont? _spriteFont;

    /// <summary>Takes its font from the root's <see cref="UiStyle"/> according to <see cref="Role"/>.</summary>
    public Label(string content = "")
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = content;
    }

    /// <summary>Uses <paramref name="font"/> regardless of the style.</summary>
    public Label(IFont font, string content = "")
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(content);

        _font = font;
        _content = content;
    }

    protected override int MaxChildCount => 0;

    public string Content
    {
        get => _content;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (_content == value)
            {
                return;
            }

            _content = value;
            _sprite = null;
            InvalidateMeasure();
        }
    }

    /// <summary>An explicit font, or null to take one from the style.</summary>
    public IFont? Font
    {
        get => _font;
        set
        {
            if (ReferenceEquals(_font, value))
            {
                return;
            }

            _font = value;
            _sprite = null;
            InvalidateMeasure();
        }
    }

    /// <summary>Which style font to use. Ignored when <see cref="Font"/> is set.</summary>
    public TextRole Role
    {
        get => _role;
        set
        {
            if (_role == value)
            {
                return;
            }

            _role = value;
            _sprite = null;
            InvalidateMeasure();
        }
    }

    public Color Color
    {
        get => _color;
        set => SetPaintProperty(ref _color, value);
    }

    protected override Vector2Int MeasureContent(Constraints constraints)
    {
        if (_content.Length == 0)
        {
            return default;
        }

        ShortSize size = ResolveFont().Measure(_content);
        return new Vector2Int(size.Width, size.Height);
    }

    protected override void PaintContent(PaintContext context)
    {
        TextSpriteAsset? sprite = ResolveSprite();
        if (sprite == null)
        {
            return;
        }

        context.DrawSprite(sprite, new Rectangle(Bounds.X, Bounds.Y, sprite.Size.X, sprite.Size.Y), _color);
    }

    private TextSpriteAsset? ResolveSprite()
    {
        if (_content.Length == 0)
        {
            return null;
        }

        // The font is resolved every time rather than only on a cache miss, because a style
        // replaced on the root — or a subtree moved to a root with a different one — changes it
        // without anything reaching this label. The walk is an ancestor chain and no allocation.
        IFont font = ResolveFont();

        if (_sprite == null || !ReferenceEquals(font, _spriteFont))
        {
            _sprite = font.CreateTextSprite(_content);
            _spriteFont = font;
        }

        return _sprite;
    }

    private IFont ResolveFont()
    {
        if (_font != null)
        {
            return _font;
        }

        UiStyle? style = OwnerRoot?.Style;

        IFont? font = _role switch
        {
            TextRole.Title => style?.Title,
            TextRole.Small => style?.Small,
            _ => style?.Body
        };

        return font ?? throw new InvalidOperationException(
            $"A {nameof(Label)} without an explicit Font needs a UiStyle with a {_role} font on the UiRoot it belongs to. " +
            "Set UiRoot.Style, or construct the label with a font.");
    }
}
