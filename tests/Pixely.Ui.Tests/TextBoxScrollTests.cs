namespace Pixely.Ui.Tests;

/// <summary>
/// Where a field slides its text to so the caret stays visible. Separated from the field itself
/// because everything else about drawing one needs a font that can rasterise — this is the part
/// that can be checked on its own, and it is the part that gets edge cases wrong.
/// </summary>
public class TextBoxScrollTests
{
    [Test]
    public void TextThatFits_DoesNotScroll()
    {
        Assert.That(Offset(caretX: 30, textWidth: 40, contentWidth: 100, currentOffset: 0), Is.Zero);
    }

    [Test]
    public void ACaretAtTheStart_ScrollsBackToTheBeginning()
    {
        Assert.That(Offset(caretX: 0, textWidth: 400, contentWidth: 100, currentOffset: 250), Is.Zero);
    }

    [Test]
    public void ACaretPastTheRightEdge_ScrollsJustFarEnoughToShowIt()
    {
        // One further than the edge itself: the caret is a rectangle starting at its own coordinate,
        // so a caret sitting exactly on the trailing edge is drawn entirely outside the field.
        Assert.That(Offset(caretX: 400, textWidth: 400, contentWidth: 100, currentOffset: 0), Is.EqualTo(301));
    }

    [Test]
    public void ACaretAtTheVeryEndOfTextThatExactlyFits_IsStillVisible()
    {
        int offset = Offset(caretX: 100, textWidth: 100, contentWidth: 100, currentOffset: 0);

        Assert.That(offset, Is.EqualTo(1), "otherwise the caret at the end of a full field is clipped away");
    }

    [Test]
    public void ACaretLeftOfTheView_ScrollsBackToIt()
    {
        Assert.That(Offset(caretX: 50, textWidth: 400, contentWidth: 100, currentOffset: 200), Is.EqualTo(50));
    }

    [Test]
    public void ACaretAlreadyInView_LeavesTheOffsetAlone()
    {
        Assert.That(Offset(caretX: 250, textWidth: 400, contentWidth: 100, currentOffset: 200), Is.EqualTo(200));
    }

    [Test]
    public void AnOffsetPastTheEndOfShrunkenText_IsBroughtBack()
    {
        // What deleting leaves behind: the offset was right for the text that used to be there.
        Assert.That(Offset(caretX: 20, textWidth: 120, contentWidth: 100, currentOffset: 300), Is.EqualTo(20));
    }

    [Test]
    public void AFieldWithNoRoomAtAll_DoesNotScroll()
    {
        Assert.That(Offset(caretX: 40, textWidth: 400, contentWidth: 0, currentOffset: 10), Is.Zero);
    }

    private static int Offset(int caretX, int textWidth, int contentWidth, int currentOffset) =>
        TextBox.ScrollOffsetFor(caretX, textWidth, contentWidth, currentOffset);
}
