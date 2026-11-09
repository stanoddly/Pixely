using Pixely.Gpu;
using Pixely.Testing;
using Pixely.Utilities;

namespace Pixely.Tests;

public class MultisamplingTests
{
    private static readonly ShortSize _size = new ShortSize(64, 32);

    private GpuDevice _gpuDevice = null!;

    [SetUp]
    public void SetUp()
    {
        _gpuDevice = StubGpuDevice.Create();
    }

    [TearDown]
    public void TearDown()
    {
        _gpuDevice.Dispose();
    }

    [Test]
    public void CreateRenderPass_WithMatchingAttachments_TakesTheirSampleCount()
    {
        Texture[] colorTargets = [CreateTexture(SampleCount.Count4), CreateTexture(SampleCount.Count4)];

        Assert.That(GetSampleCount(colorTargets, CreateDepthBuffer(SampleCount.Count4)), Is.EqualTo(SampleCount.Count4));
    }

    [Test]
    public void CreateRenderPass_WithDepthBufferOnly_TakesItsSampleCount()
    {
        Assert.That(GetSampleCount([], CreateDepthBuffer(SampleCount.Count2)), Is.EqualTo(SampleCount.Count2));
    }

    [Test]
    public void CreateRenderPass_WithMismatchedColorTargets_Throws()
    {
        Texture[] colorTargets = [CreateTexture(SampleCount.Count4), CreateTexture(SampleCount.Count1)];

        Assert.Throws<InvalidOperationException>(() => GetSampleCount(colorTargets, null));
    }

    [Test]
    public void CreateRenderPass_WithMismatchedDepthBuffer_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => GetSampleCount([CreateTexture(SampleCount.Count4)], CreateDepthBuffer(SampleCount.Count1)));
    }

    [TestCase(StoreOperation.Resolve)]
    [TestCase(StoreOperation.ResolveAndStore)]
    public void CreateRenderPass_WithMatchingResolveTexture_Succeeds(StoreOperation storeOperation)
    {
        Assert.DoesNotThrow(() => CreateResolvingRenderPass(CreateTexture(SampleCount.Count4), storeOperation, CreateTexture(SampleCount.Count1)));
    }

    [Test]
    public void CreateRenderPass_WithoutResolveTextureOrResolve_Succeeds()
    {
        Assert.DoesNotThrow(() => CreateResolvingRenderPass(CreateTexture(SampleCount.Count4), StoreOperation.Store, null));
    }

    [Test]
    public void CreateRenderPass_WithResolveStoreOperationAndNoResolveTexture_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => CreateResolvingRenderPass(CreateTexture(SampleCount.Count4), StoreOperation.Resolve, null));
    }

    [Test]
    public void CreateRenderPass_WithResolveTextureAndStoreOperationStore_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => CreateResolvingRenderPass(CreateTexture(SampleCount.Count4), StoreOperation.Store, CreateTexture(SampleCount.Count1)));
    }

    [Test]
    public void CreateRenderPass_ResolvingASingleSampleColorTarget_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => CreateResolvingRenderPass(CreateTexture(SampleCount.Count1), StoreOperation.Resolve, CreateTexture(SampleCount.Count1)));
    }

    [Test]
    public void CreateRenderPass_WithMultisampledResolveTexture_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => CreateResolvingRenderPass(CreateTexture(SampleCount.Count4), StoreOperation.Resolve, CreateTexture(SampleCount.Count4)));
    }

    [Test]
    public void CreateRenderPass_WithResolveTextureOfDifferentFormat_Throws()
    {
        Texture resolveTexture = CreateTexture(SampleCount.Count1, TextureFormat.R8G8B8A8Unorm);

        Assert.Throws<InvalidOperationException>(() => CreateResolvingRenderPass(CreateTexture(SampleCount.Count4), StoreOperation.Resolve, resolveTexture));
    }

    [Test]
    public void CreateRenderPass_WithResolveTextureOfDifferentSize_Throws()
    {
        Texture resolveTexture = CreateTexture(SampleCount.Count1, TextureFormat.B8G8R8A8Unorm, new ShortSize(32, 32));

        Assert.Throws<InvalidOperationException>(() => CreateResolvingRenderPass(CreateTexture(SampleCount.Count4), StoreOperation.Resolve, resolveTexture));
    }

    [Test]
    public void CreateRenderPass_WithResolveTextureWithoutColorTargetUsage_Throws()
    {
        Texture resolveTexture = CreateTexture(SampleCount.Count1, usage: TextureUsage.Sampler);

        Assert.Throws<InvalidOperationException>(() => CreateResolvingRenderPass(CreateTexture(SampleCount.Count4), StoreOperation.Resolve, resolveTexture));
    }

    [TestCase(StoreOperation.Store, StoreOperation.Store)]
    [TestCase(StoreOperation.DontCare, StoreOperation.DontCare)]
    public void ValidateDepthBufferStoreOperations_WithStoreOrDontCare_Succeeds(StoreOperation depth, StoreOperation stencil)
    {
        DepthBufferSettings settings = new() { DepthBufferStoreOperation = depth, StencilStoreOperation = stencil };

        Assert.DoesNotThrow(() => CommandBuffer.ValidateDepthBufferStoreOperations(settings));
    }

    [TestCase(StoreOperation.Resolve, StoreOperation.Store)]
    [TestCase(StoreOperation.ResolveAndStore, StoreOperation.Store)]
    [TestCase(StoreOperation.Store, StoreOperation.Resolve)]
    [TestCase(StoreOperation.Store, StoreOperation.ResolveAndStore)]
    public void ValidateDepthBufferStoreOperations_WithResolve_Throws(StoreOperation depth, StoreOperation stencil)
    {
        DepthBufferSettings settings = new() { DepthBufferStoreOperation = depth, StencilStoreOperation = stencil };

        Assert.Throws<InvalidOperationException>(() => CommandBuffer.ValidateDepthBufferStoreOperations(settings));
    }

    [TestCase(-1)]
    [TestCase(4)]
    public void IsSampleCountSupported_WithUndefinedSampleCount_Throws(int value)
    {
        using GpuDevice gpuDevice = StubGpuDevice.Create();

        Assert.Throws<ArgumentOutOfRangeException>(() => gpuDevice.IsSampleCountSupported(TextureFormat.B8G8R8A8Unorm, (SampleCount)value));
    }

    [Test]
    public void CreateTexture_SizeInBytes_CountsEverySample()
    {
        long singleSampleBytes = TextureFormat.B8G8R8A8Unorm.CalculateSizeInBytes(_size.Width, _size.Height);

        Assert.Multiple(() =>
        {
            Assert.That(CreateTexture(SampleCount.Count1).SizeInBytes, Is.EqualTo(singleSampleBytes));
            Assert.That(CreateTexture(SampleCount.Count4).SizeInBytes, Is.EqualTo(singleSampleBytes * 4));
            Assert.That(CreateTexture(SampleCount.Count8).SizeInBytes, Is.EqualTo(singleSampleBytes * 8));
        });
    }

    private SampleCount GetSampleCount(Texture[] colorTargets, Texture? depthBuffer)
    {
        ColorTargetSettings[] colorTargetSettings = colorTargets.Select(_ => ColorTargetSettings.Clear).ToArray();
        using CommandBuffer commandBuffer = _gpuDevice.AcquireCommandBuffer();
        using RenderPass renderPass = commandBuffer.CreateRenderPass(colorTargets, colorTargetSettings, depthBuffer, new DepthBufferSettings());
        return renderPass.SampleCount;
    }

    private void CreateResolvingRenderPass(Texture colorTarget, StoreOperation storeOperation, Texture? resolveTexture)
    {
        using CommandBuffer commandBuffer = _gpuDevice.AcquireCommandBuffer();
        using RenderPass renderPass = commandBuffer.CreateRenderPass([colorTarget], [new ColorTargetSettings { StoreOperation = storeOperation }], null, new DepthBufferSettings(), [resolveTexture]);
    }

    private Texture CreateDepthBuffer(SampleCount sampleCount)
    {
        return _gpuDevice.CreateTexture(_size, TextureFormat.D16Unorm, TextureUsage.DepthStencilTarget, sampleCount);
    }

    private Texture CreateTexture(SampleCount sampleCount, TextureFormat format = TextureFormat.B8G8R8A8Unorm, ShortSize? size = null, TextureUsage usage = TextureUsage.ColorTarget)
    {
        return _gpuDevice.CreateTexture(size ?? _size, format, usage, sampleCount);
    }
}
