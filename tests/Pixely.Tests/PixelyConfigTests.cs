namespace Pixely.Tests;

public sealed class PixelyConfigTests
{
    [Test]
    public void Defaults_DoNotSpecifyTaskbarPresentation()
    {
        PixelyConfig config = new();

        Assert.Multiple(() =>
        {
            Assert.That(config.ApplicationIdentifier, Is.Null);
            Assert.That(config.TaskbarIconPath, Is.Null);
        });
    }

    [Test]
    public void TaskbarPresentation_CanBeConfigured()
    {
        PixelyConfig config = new(
            ApplicationIdentifier: "com.example.mygame",
            TaskbarIconPath: "images/taskbar-icon.png");

        Assert.Multiple(() =>
        {
            Assert.That(config.ApplicationIdentifier, Is.EqualTo("com.example.mygame"));
            Assert.That(config.TaskbarIconPath, Is.EqualTo("images/taskbar-icon.png"));
        });
    }
}
