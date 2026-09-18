using System.Runtime.CompilerServices;
using Pixely.App;
using Pixely.DependencyInjection;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Shaders;
using Pixely.Text;

namespace Pixely.Tests;

public class GpuExtensionsTests
{
    [Test]
    public void UseGpu_RegistersTheDeviceAndEveryServiceThatNeedsIt()
    {
        ServiceCollection services = new();

        services.UseGpu();

        Assert.Multiple(() =>
        {
            Assert.That(services.IsRegistered<GpuDevice>(), Is.True);
            Assert.That(services.IsRegistered<GpuMemorySystem>(), Is.True);
            Assert.That(services.IsRegistered<ShaderLoader>(), Is.True);
            Assert.That(services.IsRegistered<IShaderLoader>(), Is.True);
            Assert.That(services.IsRegistered<ITextureLoader>(), Is.True);
            Assert.That(services.IsRegistered<GraphicsPipelineBuilder>(), Is.True);
            Assert.That(services.IsRegistered<ComputeShaderLoader>(), Is.True);
            Assert.That(services.IsRegistered<IComputeShaderLoader>(), Is.True);
            Assert.That(services.IsRegistered<ComputePipelineBuilder>(), Is.True);
            Assert.That(services.IsRegistered<FontSystem>(), Is.True);
            Assert.That(services.IsRegistered<IFontSystem>(), Is.True);
        });
    }

    [Test]
    public void UseGpu_DeviceAlreadyRegistered_RegistersNothing()
    {
        ServiceCollection services = new();
        services.AddSingleton(CreateGpuDeviceStub());

        services.UseGpu();

        Assert.That(services.IsRegistered<GpuMemorySystem>(), Is.False);
    }

    [Test]
    public void UseGpu_DeviceAlreadyRegistered_BuildsWithThatDevice()
    {
        ServiceCollection services = new();
        GpuDevice gpuDevice = CreateGpuDeviceStub();
        services.AddSingleton(gpuDevice);
        services.UseGpu();
        services.UseGpu();

        // Not disposed: the uninitialized stub cannot survive GpuDevice.Dispose.
        ServiceProvider provider = services.BuildServiceProvider();

        Assert.That(provider.GetServices<GpuDevice>(), Is.EqualTo(new[] { gpuDevice }));
    }

    [Test]
    public void AddWindow_DoesNotRegisterTheGpuDevice()
    {
        PixelyAppBuilder builder = new();
        builder.AddWindow();

        Assert.That(builder.IsRegistered<GpuDevice>(), Is.False);
    }

    [Test]
    public void UseDefaultRendering_RegistersTheGpuDevice()
    {
        PixelyAppBuilder builder = new();

        builder.UseDefaultRendering();

        Assert.That(builder.IsRegistered<GpuDevice>(), Is.True);
    }

    [Test]
    public void UseWindowRendering_RegistersTheGpuDevice()
    {
        PixelyAppBuilder builder = new();

        builder.UseWindowRendering<BasicRenderContext>();

        Assert.That(builder.IsRegistered<GpuDevice>(), Is.True);
    }

    [Test]
    public void RegisterSpriteLoading_RegistersTheGpuDevice()
    {
        PixelyAppBuilder builder = new();

        builder.RegisterSpriteLoading();

        Assert.That(builder.IsRegistered<GpuDevice>(), Is.True);
    }

    private static GpuDevice CreateGpuDeviceStub()
    {
        return (GpuDevice)RuntimeHelpers.GetUninitializedObject(typeof(GpuDevice));
    }
}
