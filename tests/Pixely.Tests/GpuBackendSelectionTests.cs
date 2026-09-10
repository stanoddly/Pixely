namespace Pixely.Tests;

public class GpuBackendSelectionTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ResolveGpuBackend_WithoutEnvironmentOverride_UsesConfiguredBackend(string? environmentBackend)
    {
        GpuBackend result = PixelyFactory.ResolveGpuBackend(GpuBackend.Direct3D12, environmentBackend);

        Assert.That(result, Is.EqualTo(GpuBackend.Direct3D12));
    }

    [TestCase("automatic", GpuBackend.Automatic)]
    [TestCase("vulkan", GpuBackend.Vulkan)]
    [TestCase("direct3d12", GpuBackend.Direct3D12)]
    [TestCase("metal", GpuBackend.Metal)]
    [TestCase("webgpu", GpuBackend.WebGpu)]
    [TestCase(" VULKAN ", GpuBackend.Vulkan)]
    public void ResolveGpuBackend_WithSupportedEnvironmentOverride_UsesEnvironmentBackend(
        string environmentBackend,
        GpuBackend expected)
    {
        GpuBackend result = PixelyFactory.ResolveGpuBackend(GpuBackend.Automatic, environmentBackend);

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void ResolveGpuBackend_WithUnsupportedEnvironmentOverride_Throws()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => PixelyFactory.ResolveGpuBackend(GpuBackend.Automatic, "opengl"))!;

        Assert.That(exception.Message, Does.Contain("PIXELY_GRAPHICS"));
        Assert.That(exception.Message, Does.Contain("opengl"));
    }
}
