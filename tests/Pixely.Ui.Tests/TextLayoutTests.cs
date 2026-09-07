using Pixely.Text;

namespace Pixely.Ui.Tests;

/// <summary>
/// Text taking part in layout, with a <see cref="FixedWidthFont"/> standing in for a real one. The
/// point of the fake is twofold: the geometry is arithmetic a test can state, and rasterising
/// throws, so measuring by way of a texture would fail here rather than pass unnoticed.
/// </summary>
public class TextLayoutTests
{
    [Test]
    public void Label_MeasuresToTheWidthOfItsContent()
    {
        Label label = new(new FixedWidthFont(), "abc");

        Vector2Int size = Layout.MeasureUnbounded(label);

        Assert.That(size, Is.EqualTo(new Vector2Int(3 * FixedWidthFont.CharacterWidth, FixedWidthFont.LineHeight)));
    }

    [Test]
    public void Label_WithoutContent_MeasuresToNothingAndDoesNotAskTheFont()
    {
        FixedWidthFont font = new();
        Label label = new(font, "");

        Vector2Int size = Layout.MeasureUnbounded(label);

        Assert.That(size, Is.EqualTo(Vector2Int.Zero));
        Assert.That(font.MeasureCount, Is.EqualTo(0));
    }

    [Test]
    public void Label_TakesItsFontFromTheStyleRole()
    {
        Label body = new("abc");
        Label title = new("abc") { Role = TextRole.Title };
        Column root = new() { Children = { body, title } };
        UiRoot uiRoot = new()
        {
            Style = new UiStyle { Body = new FixedWidthFont(4), Title = new FixedWidthFont(20) }
        };
        uiRoot.AddLayer(root);

        // Laid out directly rather than through UiRoot.Update, which would also paint, and painting
        // is the one thing this font cannot do.
        Layout.Run(root, 200, 100);

        Assert.That(body.Bounds.Width, Is.EqualTo(12));
        Assert.That(title.Bounds.Width, Is.EqualTo(60));
    }

    [Test]
    public void Labels_InARow_ArePlacedAfterOneAnother()
    {
        IFont font = new FixedWidthFont();
        Label first = new(font, "ab");
        Label second = new(font, "cde");
        Row root = new() { Children = { first, second } };

        Layout.Run(root, 200, 100);

        Assert.That(first.Bounds.X, Is.EqualTo(0));
        Assert.That(first.Bounds.Width, Is.EqualTo(16));
        Assert.That(second.Bounds.X, Is.EqualTo(16));
        Assert.That(second.Bounds.Width, Is.EqualTo(24));
    }

    [Test]
    public void TextBox_MeasuresToItsValue()
    {
        TextBox field = new(new FixedWidthFont()) { Text = "abcd", Padding = new Thickness(0) };

        Vector2Int size = Layout.MeasureUnbounded(field);

        Assert.That(size, Is.EqualTo(new Vector2Int(4 * FixedWidthFont.CharacterWidth, FixedWidthFont.LineHeight)));
    }
}
