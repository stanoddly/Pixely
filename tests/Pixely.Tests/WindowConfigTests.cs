namespace Pixely.Tests;

public sealed class WindowConfigTests
{
    [Test]
    public void Defaults_AreInitiallyVisible()
    {
        Assert.That(new WindowConfig().InitiallyVisible, Is.True);
    }

    [Test]
    public void Configuration_CanStartHidden()
    {
        Assert.That(new WindowConfig(InitiallyVisible: false).InitiallyVisible, Is.False);
    }
}
