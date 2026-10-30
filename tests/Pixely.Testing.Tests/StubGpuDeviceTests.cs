using System.Numerics;
using Pixely.Content;
using Pixely.Gpu;

namespace Pixely.Testing.Tests;

public class StubGpuDeviceTests
{
    private static readonly PositionVertex[] Vertices = [new(new Vector3(1, 2, 3)), new(new Vector3(4, 5, 6))];

    [Test]
    public void ReadVertexBuffer_BeforeTheUploadsAreSubmitted_ReadsZeros()
    {
        using StubGpuDevice device = StubGpuDevice.Create();
        using GpuMemorySystem gpuMemorySystem = new(device);

        GpuVertexBuffer<PositionVertex> vertexBuffer = gpuMemorySystem.CreateVertexBuffer<PositionVertex>(Vertices);

        Assert.That(device.ReadVertexBuffer(vertexBuffer), Is.EqualTo(new PositionVertex[2]));
    }

    [Test]
    public void ReadVertexBuffer_AfterSubmit_ReadsTheUploadedVertices()
    {
        using StubGpuDevice device = StubGpuDevice.Create();
        using GpuMemorySystem gpuMemorySystem = new(device);
        GpuVertexBuffer<PositionVertex> vertexBuffer = gpuMemorySystem.CreateVertexBuffer<PositionVertex>(Vertices);

        gpuMemorySystem.Submit();

        Assert.That(device.ReadVertexBuffer(vertexBuffer), Is.EqualTo(Vertices));
    }

    [Test]
    public void ReadVertexBuffer_AfterAShorterUpdate_ReadsTheUpdatedSize()
    {
        using StubGpuDevice device = StubGpuDevice.Create();
        using GpuMemorySystem gpuMemorySystem = new(device);
        GpuVertexBuffer<PositionVertex> vertexBuffer = gpuMemorySystem.CreateVertexBuffer<PositionVertex>(Vertices);
        PositionVertex[] update = [new(new Vector3(7, 8, 9))];

        gpuMemorySystem.UpdateVertexBuffer<PositionVertex>(vertexBuffer, update);
        gpuMemorySystem.Submit();

        Assert.That(device.ReadVertexBuffer(vertexBuffer), Is.EqualTo(update));
    }

    [Test]
    public void ReadIndexBuffer_With16BitIndices_WidensThem()
    {
        using StubGpuDevice device = StubGpuDevice.Create();
        using GpuMemorySystem gpuMemorySystem = new(device);
        GpuIndexBuffer indexBuffer = gpuMemorySystem.CreateIndexBuffer(new ushort[] { 0, 1, 2, 65535 });

        gpuMemorySystem.Submit();

        Assert.That(device.ReadIndexBuffer(indexBuffer), Is.EqualTo(new uint[] { 0, 1, 2, 65535 }));
    }

    [Test]
    public void ReadStorageBuffer_AfterSubmit_ReadsTheUploadedElements()
    {
        using StubGpuDevice device = StubGpuDevice.Create();
        using GpuMemorySystem gpuMemorySystem = new(device);
        GpuStorageBuffer<int> storageBuffer = gpuMemorySystem.CreateStorageBuffer<int>(new[] { 3, 1, 4 });

        gpuMemorySystem.Submit();

        Assert.That(device.ReadStorageBuffer(storageBuffer), Is.EqualTo(new[] { 3, 1, 4 }));
    }

    [Test]
    public void ReadTexture_AfterSubmit_ReadsTheUploadedPixels()
    {
        using StubGpuDevice device = StubGpuDevice.Create();
        using GpuMemorySystem gpuMemorySystem = new(device);
        byte[] pixels = [1, 2, 3, 4, 5, 6, 7, 8];
        Texture texture = gpuMemorySystem.CreateTexture(new RawImage(pixels, new ShortSize(2, 1), PixelFormat.Rgba8888));

        gpuMemorySystem.Submit();

        Assert.That(device.ReadTexture(texture), Is.EqualTo(pixels));
    }

    [Test]
    public void ReadTexture_OfATextureArray_ReadsTheLayer()
    {
        using StubGpuDevice device = StubGpuDevice.Create();
        using GpuMemorySystem gpuMemorySystem = new(device);
        RawImage first = new([1, 1, 1, 1], new ShortSize(1, 1), PixelFormat.Rgba8888);
        RawImage second = new([2, 2, 2, 2], new ShortSize(1, 1), PixelFormat.Rgba8888);
        TextureArray textureArray = gpuMemorySystem.CreateTextureArray([first, second]);

        gpuMemorySystem.Submit();

        Assert.Multiple(() =>
        {
            Assert.That(device.ReadTexture(textureArray, 1), Is.EqualTo(new byte[] { 2, 2, 2, 2 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => device.ReadTexture(textureArray, 2));
        });
    }

    [Test]
    public void Submit_OfAnUploadToAReleasedTexture_KeepsNoContents()
    {
        using StubGpuDevice device = StubGpuDevice.Create();
        using GpuMemorySystem gpuMemorySystem = new(device);
        Texture texture = gpuMemorySystem.CreateTexture(new RawImage([9, 8, 7, 6], new ShortSize(1, 1), PixelFormat.Rgba8888));

        texture.Dispose();
        gpuMemorySystem.Submit();

        Assert.That(device.TextureContentCount, Is.Zero);
    }

    [Test]
    public void Dispose_OfATextureArray_ReleasesTheContentsOfEveryLayer()
    {
        using StubGpuDevice device = StubGpuDevice.Create();
        using GpuMemorySystem gpuMemorySystem = new(device);
        RawImage first = new([1, 1, 1, 1], new ShortSize(1, 1), PixelFormat.Rgba8888);
        RawImage second = new([2, 2, 2, 2], new ShortSize(1, 1), PixelFormat.Rgba8888);
        TextureArray textureArray = gpuMemorySystem.CreateTextureArray([first, second]);
        gpuMemorySystem.Submit();

        textureArray.Dispose();

        Assert.That(device.TextureContentCount, Is.Zero);
    }

    [Test]
    public void ReadTexture_OfAColorTarget_ReadsZeros()
    {
        using StubGpuDevice device = StubGpuDevice.Create();

        Texture colorTarget = device.CreateColorTargetTexture(new ShortSize(2, 2), TextureFormat.R8G8B8A8Unorm);

        Assert.That(device.ReadTexture(colorTarget), Is.EqualTo(new byte[16]));
    }

    [Test]
    public void Dispose_OfTheMemorySystemBeforeSubmit_CancelsTheUploads()
    {
        using StubGpuDevice device = StubGpuDevice.Create();
        GpuMemorySystem gpuMemorySystem = new(device);
        GpuVertexBuffer<PositionVertex> vertexBuffer = gpuMemorySystem.CreateVertexBuffer<PositionVertex>(Vertices);

        gpuMemorySystem.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(device.ReadVertexBuffer(vertexBuffer), Is.EqualTo(new PositionVertex[2]));
            Assert.That(device.SubmittedCommandBuffers, Is.Empty);
        });
    }

    [Test]
    public void Submit_OfTheMemorySystem_RecordsItsCommandBuffer()
    {
        using StubGpuDevice device = StubGpuDevice.Create();
        using GpuMemorySystem gpuMemorySystem = new(device);
        gpuMemorySystem.CreateVertexBuffer<PositionVertex>(Vertices);

        gpuMemorySystem.Submit();

        StubCommandBuffer commandBuffer = device.SubmittedCommandBuffers.Single();
        Assert.Multiple(() =>
        {
            Assert.That(commandBuffer.IsSubmitted, Is.True);
            Assert.That(commandBuffer.RenderPasses, Is.Empty);
        });
    }

    [Test]
    public void ClearSubmittedCommandBuffers_ForgetsThem()
    {
        using StubGpuDevice device = StubGpuDevice.Create();
        device.AcquireCommandBuffer().Submit();

        device.ClearSubmittedCommandBuffers();

        Assert.That(device.SubmittedCommandBuffers, Is.Empty);
    }

    [Test]
    public void SubmitAndDownloadTexture_ReturnsTheUploadedPixels()
    {
        using StubGpuDevice device = StubGpuDevice.Create();
        using GpuMemorySystem gpuMemorySystem = new(device);
        byte[] pixels = [9, 8, 7, 6];
        Texture texture = gpuMemorySystem.CreateTexture(new RawImage(pixels, new ShortSize(1, 1), PixelFormat.Rgba8888));
        gpuMemorySystem.Submit();

        using Image image = device.AcquireCommandBuffer().SubmitAndDownloadTexture(texture);

        Assert.That(image.Data.ToArray(), Is.EqualTo(pixels));
    }

    [Test]
    public void SubmitAndDownloadTexture_FinishesTheCommandBuffer()
    {
        using StubGpuDevice device = StubGpuDevice.Create();
        Texture texture = device.CreateColorTargetTexture(new ShortSize(1, 1), TextureFormat.R8G8B8A8Unorm);
        StubCommandBuffer commandBuffer = (StubCommandBuffer)device.AcquireCommandBuffer();

        using Image image = commandBuffer.SubmitAndDownloadTexture(texture);
        commandBuffer.Cancel();

        Assert.Multiple(() =>
        {
            Assert.That(commandBuffer.IsCancelled, Is.False);
            Assert.That(device.SubmittedCommandBuffers, Has.Count.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => commandBuffer.Submit());
        });
    }

    [Test]
    public void MemoryStats_CountsResourcesUntilTheyAreDisposed()
    {
        using StubGpuDevice device = StubGpuDevice.Create();
        Texture texture = device.CreateColorTargetTexture(new ShortSize(4, 4), TextureFormat.R8G8B8A8Unorm);

        GpuMemoryStats created = device.MemoryStats;
        texture.Dispose();
        GpuMemoryStats disposed = device.MemoryStats;

        Assert.Multiple(() =>
        {
            Assert.That(created.TextureCount, Is.EqualTo(1));
            Assert.That(created.TextureBytes, Is.EqualTo(64));
            Assert.That(disposed.TextureCount, Is.Zero);
        });
    }

    [Test]
    public void Dispose_ReleasesEveryResource()
    {
        StubGpuDevice device = StubGpuDevice.Create();
        Texture texture = device.CreateColorTargetTexture(new ShortSize(4, 4), TextureFormat.R8G8B8A8Unorm);

        device.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(device.MemoryStats.TextureCount, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => device.ReadTexture(texture));
        });
    }

    [Test]
    public void CreateTexture_WithAFormatTheOptionsDoNotSupport_Throws()
    {
        using StubGpuDevice device = StubGpuDevice.Create(new StubGpuDeviceOptions
        {
            IsTextureFormatSupported = (format, _, _) => format != TextureFormat.R16G16B16A16Float
        });

        Assert.Throws<ArgumentException>(() => device.CreateColorTargetTexture(new ShortSize(4, 4), TextureFormat.R16G16B16A16Float));
    }

    [Test]
    public void CreateTexture_WithASampleCountTheOptionsDoNotSupport_Throws()
    {
        using StubGpuDevice device = StubGpuDevice.Create(new StubGpuDeviceOptions
        {
            IsSampleCountSupported = (_, sampleCount) => sampleCount <= SampleCount.Count4
        });

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => device.CreateColorTargetTexture(new ShortSize(4, 4), TextureFormat.R8G8B8A8Unorm, SampleCount.Count4).Dispose());
            Assert.Throws<ArgumentException>(() => device.CreateColorTargetTexture(new ShortSize(4, 4), TextureFormat.R8G8B8A8Unorm, SampleCount.Count8));
        });
    }

    [Test]
    public void CreateRenderPass_RunsPixelyValidation()
    {
        using StubGpuDevice device = StubGpuDevice.Create();
        Texture singleSample = device.CreateColorTargetTexture(new ShortSize(4, 4), TextureFormat.R8G8B8A8Unorm);
        Texture multisampled = device.CreateColorTargetTexture(new ShortSize(4, 4), TextureFormat.R8G8B8A8Unorm, SampleCount.Count4);
        using CommandBuffer commandBuffer = device.AcquireCommandBuffer();

        Assert.Throws<InvalidOperationException>(() => new RenderPassBuilder(commandBuffer)
            .AddColorTarget(singleSample)
            .AddColorTarget(multisampled)
            .SetSharedColorTargetSettings(ColorTargetSettings.Clear)
            .Build());
    }

    [Test]
    public void CreateRenderPass_RecordsTheAttachments()
    {
        using StubGpuDevice device = StubGpuDevice.Create();
        SwapchainTexture colorTarget = device.CreateSwapchainTexture(new ShortSize(320, 200));
        Texture depthBuffer = device.CreateDepthBufferTexture(new ShortSize(320, 200), DepthBufferFormat.Depth24Stencil8);
        StubCommandBuffer commandBuffer = (StubCommandBuffer)device.AcquireCommandBuffer();

        using (new RenderPassBuilder(commandBuffer)
                   .AddColorTarget(colorTarget)
                   .SetSharedColorTargetSettings(ColorTargetSettings.Clear)
                   .SetDepthBuffer(depthBuffer, DepthBufferSettings.Default)
                   .Build())
        {
        }

        commandBuffer.Submit();

        StubRenderPass renderPass = commandBuffer.RenderPasses.Single();
        Assert.Multiple(() =>
        {
            Assert.That(renderPass.ColorTargets, Is.EqualTo(new Texture[] { colorTarget }));
            Assert.That(renderPass.ResolveTextures, Is.EqualTo(new Texture?[] { null }));
            Assert.That(renderPass.DepthBuffer, Is.SameAs(depthBuffer));
            Assert.That(renderPass.TargetSize, Is.EqualTo(new ShortSize(320, 200)));
            Assert.That(renderPass.IsEnded, Is.True);
            Assert.That(renderPass.Draws, Is.Empty);
        });
    }

    [Test]
    public void BlitTextures_IsRecorded()
    {
        using StubGpuDevice device = StubGpuDevice.Create();
        Texture source = device.CreateColorTargetTexture(new ShortSize(4, 4), TextureFormat.R8G8B8A8Unorm);
        SwapchainTexture destination = device.CreateSwapchainTexture(new ShortSize(4, 4));
        StubCommandBuffer commandBuffer = (StubCommandBuffer)device.AcquireCommandBuffer();

        commandBuffer.BlitTextures(source, destination);
        commandBuffer.Submit();

        Assert.That(commandBuffer.Blits, Is.EqualTo(new[] { new StubBlit(source, destination) }));
    }

    [Test]
    public void Cancel_MarksTheCommandBufferCancelledWithoutSubmittingIt()
    {
        using StubGpuDevice device = StubGpuDevice.Create();
        StubCommandBuffer commandBuffer = (StubCommandBuffer)device.AcquireCommandBuffer();

        commandBuffer.Cancel();

        Assert.Multiple(() =>
        {
            Assert.That(commandBuffer.IsCancelled, Is.True);
            Assert.That(commandBuffer.IsSubmitted, Is.False);
            Assert.That(device.SubmittedCommandBuffers, Is.Empty);
            Assert.Throws<ObjectDisposedException>(() => commandBuffer.Submit());
        });
    }

    [Test]
    public void WaitForFences_ReturnsAtOnce()
    {
        using StubGpuDevice device = StubGpuDevice.Create();
        using GpuFence fence = device.AcquireCommandBuffer().SubmitAndAcquireFence();

        device.WaitForFences([fence]);

        Assert.That(fence.IsSignaled(), Is.True);
    }
}
