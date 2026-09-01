namespace Pixely.Tests;

public class MessageBoxTests
{
    [Test]
    public void Show_WithNullTitle_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MessageBox.Show(MessageBoxSeverity.Error, null!, "message"));
    }

    [Test]
    public void Show_WithNullMessage_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MessageBox.Show(MessageBoxSeverity.Error, "title", null!));
    }

    [Test]
    public void Show_WithUnknownSeverity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MessageBox.Show((MessageBoxSeverity)(-1), "title", "message"));
    }
}
