using Pixely.App;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Ui;

namespace Pixely.Testing.Tests;

public class StubWindowTests
{
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
        PixelyAppBuilder builder = TestApp.CreateBuilder();
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
