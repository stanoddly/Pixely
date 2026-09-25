using System.Runtime.CompilerServices;
using Pixely.Gpu;

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

    [Test]
    public void ColorTargetFormat_WindowWithoutGpuDevice_Throws()
    {
        Window window = (Window)RuntimeHelpers.GetUninitializedObject(typeof(Window));

        Assert.Throws<InvalidOperationException>(() => _ = window.ColorTargetFormat);
    }

    [Test]
    public void TryWaitAndAcquireSwapchainTexture_WindowWithoutGpuDevice_Throws()
    {
        Window window = (Window)RuntimeHelpers.GetUninitializedObject(typeof(Window));

        Assert.Throws<InvalidOperationException>(() =>
        {
            CommandBuffer commandBuffer = default;
            window.TryWaitAndAcquireSwapchainTexture(ref commandBuffer, out _);
        });
    }

    private static Window CreateDisposedWindow()
    {
        Window window = (Window)RuntimeHelpers.GetUninitializedObject(typeof(Window));
        window.Dispose();
        return window;
    }
}
