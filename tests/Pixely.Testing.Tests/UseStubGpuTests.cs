using Pixely.App;
using Pixely.DependencyInjection;
using Pixely.Gpu;
using Pixely.Headless;
using Pixely.RenderOrchestration;
using Pixely.Ui;
using SDL;

namespace Pixely.Testing.Tests;

public class UseStubGpuTests
{
    [Test]
    public void UseStubGpu_RegistersTheStubAsTheDeviceWithEveryGpuService()
    {
        ServiceCollection services = new();

        services.UseStubGpu();

        Assert.Multiple(() =>
        {
            Assert.That(services.IsRegistered<StubGpuDevice>(), Is.True);
            Assert.That(services.IsRegistered<GpuDevice>(), Is.True);
            Assert.That(services.IsRegistered<GpuMemorySystem>(), Is.True);
            Assert.That(services.IsRegistered<GraphicsPipelineBuilder>(), Is.True);
        });
    }

    [Test]
    public void UseStubGpu_AfterAnotherDevice_Throws()
    {
        PixelyAppBuilder builder = new();
        builder.UseDefaultRendering();

        Assert.Throws<InvalidOperationException>(() => builder.UseStubGpu());
    }

    [Test]
    public void Build_WithTheStub_ResolvesTheStubAsTheDeviceWithAStubWindow()
    {
        PixelyAppBuilder builder = new();
        builder.UseStubGpu();
        builder.UseDefaultRendering();
        using IPixelyApp app = builder.Build();

        Assert.Multiple(() =>
        {
            Assert.That(app.GetRequiredService<GpuDevice>(), Is.SameAs(app.GetRequiredService<StubGpuDevice>()));
            Assert.That(app.GetRequiredService<Window>(), Is.InstanceOf<StubWindow>());
        });
    }

    [Test]
    public void UseStubGpu_PicksTheDummyVideoDriverOnlyWhileTheAppRuns()
    {
        string? environment = Environment.GetEnvironmentVariable("SDL_VIDEO_DRIVER");
        PixelyAppBuilder builder = new();
        builder.UseStubGpu();
        builder.UseDefaultRendering();

        string? registered = SDL3.SDL_GetHint(SDL3.SDL_HINT_VIDEO_DRIVER);
        string? running;
        using (builder.Build())
        {
            running = SDL3.SDL_GetHint(SDL3.SDL_HINT_VIDEO_DRIVER);
        }

        string? disposed = SDL3.SDL_GetHint(SDL3.SDL_HINT_VIDEO_DRIVER);

        Assert.Multiple(() =>
        {
            Assert.That(registered, Is.EqualTo(environment));
            Assert.That(running, Is.EqualTo(environment ?? "dummy"));
            Assert.That(disposed, Is.EqualTo(environment));
        });
    }

    [Test]
    public void Build_WithTheStubInHeadlessMode_Throws()
    {
        PixelyAppBuilder builder = new();
        builder.AddHeadlessSupport(name => name == HeadlessExtensions.HeadlessVariable ? "1" : null);
        builder.UseStubGpu();
        builder.UseDefaultRendering();

        Assert.Throws<PixelyInitializationException>(() => builder.Build().Dispose());
    }

    [Test]
    public void RunFrame_WithTheStub_SubmitsOnlyTheFramesCommandBuffer()
    {
        using IPixelyApp app = BuildWithUiLayer(new WindowConfig(Size: (200, 100)));
        StubGpuDevice device = app.GetRequiredService<StubGpuDevice>();
        app.RunFrame();
        device.ClearSubmittedCommandBuffers();

        app.RunFrame();

        StubCommandBuffer frame = device.SubmittedCommandBuffers.Single();
        Assert.That(frame.RenderPasses.Single().ColorTargets.Single().Size, Is.EqualTo(new ShortSize(200, 100)));
    }

    [Test]
    public void RunFrame_WithAHiddenStubWindow_RendersNothing()
    {
        using IPixelyApp app = BuildWithUiLayer(new WindowConfig(InitiallyVisible: false));

        app.RunFrame();

        Assert.That(app.GetRequiredService<StubGpuDevice>().SubmittedCommandBuffers.SelectMany(commandBuffer => commandBuffer.RenderPasses), Is.Empty);
    }

    [Test]
    public void RunFrame_WithPixelyUi_RecordsTheUisDraws()
    {
        using IPixelyApp app = BuildWithUiLayer(new WindowConfig(Size: (200, 100)));

        app.RunFrame();

        StubCommandBuffer frame = app.GetRequiredService<StubGpuDevice>().SubmittedCommandBuffers.Single(commandBuffer => commandBuffer.RenderPasses.Count > 0);
        Assert.That(frame.RenderPasses.SelectMany(renderPass => renderPass.Draws), Is.Not.Empty);
    }

    private static IPixelyApp BuildWithUiLayer(WindowConfig windowConfig)
    {
        PixelyAppBuilder builder = new();
        builder.UseStubGpu();
        builder.UseDefaultRendering(windowConfig);
        builder.UseUi();
        builder.OnBuilt(provider => provider.GetUiRoot().AddLayer(new Column
        {
            Background = new SolidDrawable(new SrgbColor(10, 20, 30, 255)),
            Width = Sizing.Grow(),
            Height = Sizing.Grow()
        }));
        return builder.Build();
    }
}
