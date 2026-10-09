using System.Runtime.CompilerServices;
using Pixely.Gpu;
using Pixely.Utilities;
using SDL;

namespace Pixely.Tests;

public class MultisamplingTests
{
    private static readonly ShortSize _size = new ShortSize(64, 32);

    [Test]
    public void ValidateSampleCounts_WithMatchingAttachments_ReturnsTheirCount()
    {
        Texture[] colorTargets = [CreateTexture(SampleCount.Count4), CreateTexture(SampleCount.Count4)];

        SampleCount sampleCount = CommandBuffer.ValidateSampleCounts(colorTargets, CreateTexture(SampleCount.Count4, TextureFormat.D16Unorm, usage: TextureUsage.DepthStencilTarget));

        Assert.That(sampleCount, Is.EqualTo(SampleCount.Count4));
    }

    [Test]
    public void ValidateSampleCounts_WithDepthBufferOnly_ReturnsItsCount()
    {
        SampleCount sampleCount = CommandBuffer.ValidateSampleCounts([], CreateTexture(SampleCount.Count2, TextureFormat.D16Unorm, usage: TextureUsage.DepthStencilTarget));

        Assert.That(sampleCount, Is.EqualTo(SampleCount.Count2));
    }

    [Test]
    public void ValidateSampleCounts_WithMismatchedColorTargets_Throws()
    {
        Texture[] colorTargets = [CreateTexture(SampleCount.Count4), CreateTexture(SampleCount.Count1)];

        Assert.Throws<InvalidOperationException>(() => CommandBuffer.ValidateSampleCounts(colorTargets, null));
    }

    [Test]
    public void ValidateSampleCounts_WithMismatchedDepthBuffer_Throws()
    {
        Texture[] colorTargets = [CreateTexture(SampleCount.Count4)];

        Assert.Throws<InvalidOperationException>(() => CommandBuffer.ValidateSampleCounts(colorTargets, CreateTexture(SampleCount.Count1, TextureFormat.D16Unorm, usage: TextureUsage.DepthStencilTarget)));
    }

    [Test]
    public void ValidateResolve_WithMatchingResolveTexture_Succeeds()
    {
        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => CommandBuffer.ValidateResolve(0, CreateTexture(SampleCount.Count4), StoreOperation.Resolve, CreateTexture(SampleCount.Count1)));
            Assert.DoesNotThrow(() => CommandBuffer.ValidateResolve(0, CreateTexture(SampleCount.Count4), StoreOperation.ResolveAndStore, CreateTexture(SampleCount.Count1)));
        });
    }

    [Test]
    public void ValidateResolve_WithoutResolveTextureOrResolve_Succeeds()
    {
        Assert.DoesNotThrow(() => CommandBuffer.ValidateResolve(0, CreateTexture(SampleCount.Count4), StoreOperation.Store, null));
    }

    [Test]
    public void ValidateResolve_WithResolveStoreOperationAndNoResolveTexture_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => CommandBuffer.ValidateResolve(0, CreateTexture(SampleCount.Count4), StoreOperation.Resolve, null));
    }

    [Test]
    public void ValidateResolve_WithResolveTextureAndStoreOperationStore_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => CommandBuffer.ValidateResolve(0, CreateTexture(SampleCount.Count4), StoreOperation.Store, CreateTexture(SampleCount.Count1)));
    }

    [Test]
    public void ValidateResolve_WithSingleSampleColorTarget_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => CommandBuffer.ValidateResolve(0, CreateTexture(SampleCount.Count1), StoreOperation.Resolve, CreateTexture(SampleCount.Count1)));
    }

    [Test]
    public void ValidateResolve_WithMultisampledResolveTexture_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => CommandBuffer.ValidateResolve(0, CreateTexture(SampleCount.Count4), StoreOperation.Resolve, CreateTexture(SampleCount.Count4)));
    }

    [Test]
    public void ValidateResolve_WithDifferentFormat_Throws()
    {
        Texture resolveTexture = CreateTexture(SampleCount.Count1, TextureFormat.R8G8B8A8Unorm);

        Assert.Throws<InvalidOperationException>(() => CommandBuffer.ValidateResolve(0, CreateTexture(SampleCount.Count4), StoreOperation.Resolve, resolveTexture));
    }

    [Test]
    public void ValidateResolve_WithDifferentSize_Throws()
    {
        Texture resolveTexture = CreateTexture(SampleCount.Count1, TextureFormat.B8G8R8A8Unorm, new ShortSize(32, 32));

        Assert.Throws<InvalidOperationException>(() => CommandBuffer.ValidateResolve(0, CreateTexture(SampleCount.Count4), StoreOperation.Resolve, resolveTexture));
    }

    [Test]
    public void ValidateResolve_WithResolveTextureWithoutColorTargetUsage_Throws()
    {
        Texture resolveTexture = CreateTexture(SampleCount.Count1, usage: TextureUsage.Sampler);

        Assert.Throws<InvalidOperationException>(() => CommandBuffer.ValidateResolve(0, CreateTexture(SampleCount.Count4), StoreOperation.Resolve, resolveTexture));
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
        GpuDevice gpuDevice = (GpuDevice)RuntimeHelpers.GetUninitializedObject(typeof(GpuDevice));

        Assert.Throws<ArgumentOutOfRangeException>(() => gpuDevice.IsSampleCountSupported(TextureFormat.B8G8R8A8Unorm, (SampleCount)value));
    }

    [Test]
    public void UserTexture_SizeInBytes_CountsEverySample()
    {
        GpuDevice gpuDevice = (GpuDevice)RuntimeHelpers.GetUninitializedObject(typeof(GpuDevice));
        long singleSampleBytes = TextureFormat.B8G8R8A8Unorm.CalculateSizeInBytes(_size.Width, _size.Height);

        Assert.Multiple(() =>
        {
            Assert.That(new UserTexture(gpuDevice, default, _size, TextureFormat.B8G8R8A8Unorm, TextureUsage.ColorTarget).SizeInBytes, Is.EqualTo(singleSampleBytes));
            Assert.That(new UserTexture(gpuDevice, default, _size, TextureFormat.B8G8R8A8Unorm, TextureUsage.ColorTarget, SampleCount.Count4).SizeInBytes, Is.EqualTo(singleSampleBytes * 4));
            Assert.That(new UserTexture(gpuDevice, default, _size, TextureFormat.B8G8R8A8Unorm, TextureUsage.ColorTarget, SampleCount.Count8).SizeInBytes, Is.EqualTo(singleSampleBytes * 8));
        });
    }

    private static Texture CreateTexture(
        SampleCount sampleCount, TextureFormat format = TextureFormat.B8G8R8A8Unorm, ShortSize? size = null, TextureUsage usage = TextureUsage.ColorTarget)
    {
        return new TestTexture(size ?? _size, format, usage, sampleCount);
    }

    private sealed class TestTexture : Texture
    {
        internal TestTexture(ShortSize size, TextureFormat format, TextureUsage usage, SampleCount sampleCount)
            : base(Pointer<SDL_GPUTexture>.Null, size, format, 0, usage, sampleCount)
        {
        }

        public override void Dispose()
        {
        }
    }
}
