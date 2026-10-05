using System.Runtime.CompilerServices;
using Pixely.App;
using Pixely.Gpu;
using Pixely.Utilities;
using SDL;

namespace Pixely.Tests;

public class FrameTimingsTests
{
    // One timestamp tick is one millisecond.
    internal const long Frequency = 1000;

    private const double Tolerance = 1e-9;

    [Test]
    public void EndFrame_RecordsTheSplitOfTheFrame()
    {
        long now = 0;
        FrameTimings timings = new(() => now, Frequency);

        RunFrame(timings, ref now, update: 200, waits: [200, 300], recording: 300);

        Assert.Multiple(() =>
        {
            Assert.That(timings.Count, Is.EqualTo(1));
            Assert.That(timings.Duration, Is.EqualTo(1.0).Within(Tolerance));
            Assert.That(timings.FrameTimes[0], Is.EqualTo(1.0).Within(Tolerance));
            Assert.That(timings.UpdateTimes[0], Is.EqualTo(0.2).Within(Tolerance));
            Assert.That(timings.RenderTimes[0], Is.EqualTo(0.3).Within(Tolerance));
            Assert.That(timings.SwapchainWaitTimes[0], Is.EqualTo(0.5).Within(Tolerance));
        });
    }

    [Test]
    public void EndFrame_MeasuresTheFrameFromTheEndOfThePreviousFrame()
    {
        long now = 0;
        FrameTimings timings = new(() => now, Frequency);

        RunFrame(timings, ref now, update: 100, waits: [], recording: 0);
        now += 900;
        RunFrame(timings, ref now, update: 100, waits: [], recording: 0);

        Assert.That(timings.FrameTimes[1], Is.EqualTo(1.0).Within(Tolerance));
    }

    [Test]
    public void EndFrame_LeavesTheInputWaitOutOfTheFrameAndTheUpdate()
    {
        long now = 0;
        FrameTimings timings = new(() => now, Frequency);

        timings.BeginFrame();
        now += 100;
        timings.BeginInputWait();
        now += 3000;
        timings.EndInputWait();
        now += 100;
        timings.EndUpdate();
        timings.EndRender();
        timings.EndFrame();

        Assert.Multiple(() =>
        {
            Assert.That(timings.FrameTimes[0], Is.EqualTo(0.2).Within(Tolerance));
            Assert.That(timings.UpdateTimes[0], Is.EqualTo(0.2).Within(Tolerance));
        });
    }

    [Test]
    public void EndFrame_WhenFull_DropsTheFrame()
    {
        long now = 0;
        FrameTimings timings = new(() => now, Frequency);

        for (int frame = 0; frame <= FrameTimings.Capacity; frame++)
        {
            RunFrame(timings, ref now, update: 1, waits: [], recording: 0);
        }

        Assert.Multiple(() =>
        {
            Assert.That(timings.IsFull, Is.True);
            Assert.That(timings.Count, Is.EqualTo(FrameTimings.Capacity));
        });
    }

    [Test]
    public void Reset_ClearsTheFrames()
    {
        long now = 0;
        FrameTimings timings = new(() => now, Frequency);
        RunFrame(timings, ref now, update: 200, waits: [], recording: 0);

        timings.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(timings.Count, Is.Zero);
            Assert.That(timings.Duration, Is.Zero);
            Assert.That(timings.FrameTimes.IsEmpty, Is.True);
        });
    }

    [Test]
    public void OnSwapchainAcquired_RecordsTheFirstSwapchainAndEachChange()
    {
        FrameTimings timings = new(() => 0, Frequency);
        Window window = UninitializedWindow<SwapchainWindow>();

        timings.OnSwapchainAcquired(window, Swapchain(1920, 1080));
        timings.OnSwapchainAcquired(window, Swapchain(1920, 1080));
        timings.OnSwapchainAcquired(window, Swapchain(1280, 720));

        Assert.That(timings.SwapchainChanges, Is.EqualTo(new[]
        {
            new SwapchainState(default, new ShortSize(1920, 1080), TextureFormat.B8G8R8A8Unorm, false),
            new SwapchainState(default, new ShortSize(1280, 720), TextureFormat.B8G8R8A8Unorm, false)
        }));
    }

    [Test]
    public void OnSwapchainAcquired_ForAnOffscreenWindow_RecordsItAsOffscreen()
    {
        FrameTimings timings = new(() => 0, Frequency);

        timings.OnSwapchainAcquired(UninitializedWindow<OffscreenWindow>(), Swapchain(800, 600));

        Assert.That(timings.SwapchainChanges.Single().Offscreen, Is.True);
    }

    [Test]
    public void ClearSwapchainChanges_KeepsTheRecordedSwapchain()
    {
        FrameTimings timings = new(() => 0, Frequency);
        Window window = UninitializedWindow<SwapchainWindow>();
        timings.OnSwapchainAcquired(window, Swapchain(1920, 1080));

        timings.ClearSwapchainChanges();
        timings.OnSwapchainAcquired(window, Swapchain(1920, 1080));

        Assert.That(timings.SwapchainChanges, Is.Empty);
    }

    internal static void RunFrame(FrameTimings timings, ref long now, long update, long[] waits, long recording)
    {
        timings.BeginFrame();
        now += update;
        timings.EndUpdate();
        foreach (long wait in waits)
        {
            timings.BeginSwapchainWait();
            now += wait;
            timings.EndSwapchainWait();
        }

        now += recording;
        timings.EndRender();
        timings.EndFrame();
    }

    internal static SwapchainTexture Swapchain(ushort width, ushort height)
    {
        return new SwapchainTexture(Pointer<SDL_GPUTexture>.Null, new ShortSize(width, height), TextureFormat.B8G8R8A8Unorm);
    }

    // Created uninitialised, so it never reaches SDL; its view scope is the default.
    internal static TWindow UninitializedWindow<TWindow>() where TWindow : Window
    {
        return (TWindow)RuntimeHelpers.GetUninitializedObject(typeof(TWindow));
    }
}
