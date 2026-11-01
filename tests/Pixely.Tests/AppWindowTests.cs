using Pixely.App;
using Pixely.Gpu;
using Pixely.Input;
using Pixely.RenderOrchestration;
using Pixely.Testing;
using SDL;

namespace Pixely.Tests;

// The app's one window and the one render coordinator every frame runs. Builds real apps on the stub GPU device, which start SDL with
// the dummy video driver.
[NonParallelizable]
public class AppWindowTests
{
    [Test]
    public void Build_KeepsARegisteredDeviceAndRegistersTheWindowAndTheGpuServicesNextToIt()
    {
        using IPixelyApp app = new PixelyAppBuilder().AddStubGpuDevice().AddStubWindows().Build();

        Assert.Multiple(() =>
        {
            Assert.That(app.GetRequiredService<GpuDevice>(), Is.SameAs(app.GetRequiredService<StubGpuDevice>()));
            Assert.That(app.GetRequiredService<Window>(), Is.TypeOf<StubWindow>());
            Assert.That(app.ServiceProvider.GetService<GpuMemorySystem>(), Is.Not.Null);
            Assert.That(app.ServiceProvider.GetService<GraphicsPipelineBuilder>(), Is.Not.Null);
        });
    }

    [Test]
    public void ConfigureWindow_TheLastCallsSettingsReachTheWindow()
    {
        PixelyAppBuilder builder = TestApp.CreateBuilder();
        builder.ConfigureWindow(new WindowConfig(Size: (640, 480)));
        builder.ConfigureWindow(new WindowConfig(Size: (320, 200)));

        using IPixelyApp app = builder.Build();

        Assert.That(app.GetRequiredService<Window>().Size, Is.EqualTo(new Size<uint>(320, 200)));
    }

    [Test]
    public void Build_WithTwoWindowFactories_Throws()
    {
        PixelyAppBuilder builder = TestApp.CreateBuilder().AddStubWindows();

        PixelyInitializationException exception = Assert.Throws<PixelyInitializationException>(() => builder.Build())!;

        Assert.That(exception.Message, Does.Contain("StubWindowFactory and StubWindowFactory"));
    }

    [Test]
    public void RunFrame_WithoutACoordinator_Throws()
    {
        using IPixelyApp app = TestApp.CreateBuilder().Build();

        PixelyInitializationException exception = Assert.Throws<PixelyInitializationException>(() => app.RunFrame())!;

        Assert.That(exception.Message, Does.Contain("no render coordinator"));
    }

    [Test]
    public void RunFrame_AfterAStageWithoutACoordinatorReplacesOneWithIt_Throws()
    {
        using IPixelyApp app = TestApp.CreateBuilder().Build();
        IStageManager stages = app.GetRequiredService<IStageManager>();
        stages.Load(static stage => stage.UseDefaultRendering());
        app.RunFrame();

        stages.Load(static _ => { });

        PixelyInitializationException exception = Assert.Throws<PixelyInitializationException>(() => app.RunFrame())!;
        Assert.That(exception.Message, Does.Contain("no render coordinator"));
    }

    [Test]
    public void RunFrame_WithAStageCoordinatorNextToTheRootOne_Throws()
    {
        PixelyAppBuilder builder = TestApp.CreateBuilder();
        builder.UseDefaultRendering();
        using IPixelyApp app = builder.Build();
        app.GetRequiredService<IStageManager>().Load(static stage => stage.UseDefaultRendering());

        PixelyInitializationException exception = Assert.Throws<PixelyInitializationException>(() => app.RunFrame())!;

        Assert.That(exception.Message, Does.Contain("more than one render coordinator"));
    }

    [Test]
    public void RunFrame_AfterACloseRequest_Quits()
    {
        PixelyAppBuilder builder = TestApp.CreateBuilder();
        builder.UseDefaultRendering();
        using IPixelyApp app = builder.Build();
        SDL_Event evt = default;
        evt.type = (uint)SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED;
        evt.window.windowID = (SDL_WindowID)app.GetRequiredService<Window>().SdlId;
        Push(evt);

        Assert.That(app.RunFrame(), Is.False);
    }

    [Test]
    public void RunFrame_IgnoresAnInputEventForAnotherWindowId()
    {
        PixelyAppBuilder builder = TestApp.CreateBuilder();
        builder.UseDefaultRendering();
        using IPixelyApp app = builder.Build();
        uint windowId = app.GetRequiredService<Window>().SdlId;
        List<Scancode> pressed = new();
        app.GetRequiredService<IKeyboardService>().KeyDown += eventArgs => pressed.Add(eventArgs.Scancode);
        PushKeyDown(windowId + 1, SDL_Scancode.SDL_SCANCODE_A);
        PushKeyDown(0, SDL_Scancode.SDL_SCANCODE_B);
        PushKeyDown(windowId, SDL_Scancode.SDL_SCANCODE_C);

        app.RunFrame();

        Assert.That(pressed, Is.EqualTo(new[] { Scancode.C }));
    }

    private static void PushKeyDown(uint windowId, SDL_Scancode scancode)
    {
        SDL_Event evt = default;
        evt.type = (uint)SDL_EventType.SDL_EVENT_KEY_DOWN;
        evt.key.windowID = (SDL_WindowID)windowId;
        evt.key.scancode = scancode;
        evt.key.down = true;
        Push(evt);
    }

    private static void Push(SDL_Event evt)
    {
        bool pushed;
        unsafe
        {
            pushed = SDL3.SDL_PushEvent(&evt);
        }

        Assert.That(pushed, Is.True, () => SDL3.SDL_GetError() ?? string.Empty);
    }
}
