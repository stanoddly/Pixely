using System.Runtime.CompilerServices;

namespace Pixely.Tests;

public class WindowTests
{
    [Test]
    public void Show_DisposedWindow_ReturnsFalse()
    {
        Window window = CreateDisposedWindow();

        Assert.That(window.Show(), Is.False);
    }

    [Test]
    public void Hide_DisposedWindow_ReturnsFalse()
    {
        Window window = CreateDisposedWindow();

        Assert.That(window.Hide(), Is.False);
    }

    [Test]
    public void Raise_DisposedWindow_ReturnsFalse()
    {
        Window window = CreateDisposedWindow();

        Assert.That(window.Raise(), Is.False);
    }

    private static Window CreateDisposedWindow()
    {
        Window window = (Window)RuntimeHelpers.GetUninitializedObject(typeof(Window));
        window.Dispose();
        return window;
    }
}
