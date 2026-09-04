using Pixely.Gpu;
using Pixely.Text;

namespace Pixely.Ui;

/// <summary>
/// A run of text. The rasterised sprite is kept until the content or font changes, so updating a
/// label costs a measure of one element rather than a rebuild of everything around it.
/// </summary>
public sealed class Text : Element
{
    private string _content;
    private Font _font;
    private Color _color = Colors.White;
    private TextSpriteAsset? _sprite;

    public Text(Font font, string content = "")
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

    public Font Font
    {
        get => _font;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (ReferenceEquals(_font, value))
            {
                return;
            }

            _font = value;
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
        TextSpriteAsset? sprite = ResolveSprite();
        return sprite == null ? default : new Vector2Int(sprite.Size.X, sprite.Size.Y);
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

        return _sprite ??= _font.FontSystem.CreateTextSprite(_content, _font);
    }
}
