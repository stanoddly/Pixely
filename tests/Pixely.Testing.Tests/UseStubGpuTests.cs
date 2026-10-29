using Pixely.App;
using Pixely.DependencyInjection;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Ui;

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
    public void Build_WithTheStub_ResolvesTheStubAsTheDeviceAndRendersIntoAnOffscreenWindow()
    {
        PixelyAppBuilder builder = new();
        builder.UseStubGpu();
        builder.UseDefaultRendering();
        using IPixelyApp app = builder.Build();

        Assert.Multiple(() =>
        {
            Assert.That(app.GetRequiredService<GpuDevice>(), Is.SameAs(app.GetRequiredService<StubGpuDevice>()));
            Assert.That(app.GetRequiredService<Window>(), Is.InstanceOf<OffscreenWindow>());
        });
    }

    [Test]
    public void RunFrame_WithPixelyUi_RecordsTheUisDraws()
    {
        PixelyAppBuilder builder = new();
        builder.UseStubGpu();
        builder.UseDefaultRendering(new WindowConfig(Size: (200, 100)));
        builder.UseUi();
        builder.OnBuilt(provider => provider.GetUiRoot().AddLayer(new Column
        {
            Background = new SolidDrawable(new SrgbColor(10, 20, 30, 255)),
            Width = Sizing.Grow(),
            Height = Sizing.Grow()
        }));
        using IPixelyApp app = builder.Build();

        app.RunFrame();

        StubCommandBuffer frame = app.GetRequiredService<StubGpuDevice>().SubmittedCommandBuffers.Single(commandBuffer => commandBuffer.RenderPasses.Count > 0);
        Assert.That(frame.RenderPasses.SelectMany(renderPass => renderPass.Draws), Is.Not.Empty);
    }
}
