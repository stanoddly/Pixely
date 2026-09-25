using System.Runtime.CompilerServices;
using Pixely.Gpu;

namespace Pixely.Tests;

public class RenderPathRefStructTests
{
    [Test]
    public void CommandBuffer_Default_ThrowsOnRecording()
    {
        Assert.Throws<ObjectDisposedException>(() =>
        {
            CommandBuffer commandBuffer = default;
            commandBuffer.PushVertexUniformData(0, 1f);
        });
    }

    [Test]
    public void CommandBuffer_Default_ThrowsOnSubmit()
    {
        Assert.Throws<ObjectDisposedException>(() =>
        {
            CommandBuffer commandBuffer = default;
            commandBuffer.Submit();
        });
    }

    [Test]
    public void CommandBuffer_Default_DisposesWithoutThrowing()
    {
        Assert.DoesNotThrow(() =>
        {
            CommandBuffer commandBuffer = default;
            commandBuffer.Dispose();
        });
    }

    [Test]
    public void RenderPass_Default_IsDefaultAndDisposesWithoutThrowing()
    {
        RenderPass renderPass = default;

        Assert.That(renderPass.IsDefault(), Is.True);
        Assert.DoesNotThrow(() =>
        {
            RenderPass defaultPass = default;
            defaultPass.Dispose();
        });
    }

    [Test]
    public void RenderPass_Default_ThrowsOnUse()
    {
        Assert.Throws<ObjectDisposedException>(() =>
        {
            RenderPass renderPass = default;
            renderPass.DrawPrimitive();
        });
    }

    [Test]
    public void ComputePass_Default_ThrowsOnUse()
    {
        Assert.Throws<ObjectDisposedException>(() =>
        {
            ComputePass computePass = default;
            computePass.Dispatch(1, 1, 1);
        });
    }

    [Test]
    public void RenderPassBuilder_WithoutCommandBuffer_ThrowsOnBuild()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            RenderPassBuilder builder = default;
            builder.Build();
        });
    }

    [Test]
    public void RenderPassBuilder_WithoutTargets_ThrowsOnBuild()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            CommandBuffer commandBuffer = default;
            new RenderPassBuilder(ref commandBuffer).Build();
        });
    }

    [Test]
    public void RenderPassBuilder_ChainedTargetsOnDisposedCommandBuffer_ReachesTheCommandBuffer()
    {
        Texture texture = CreateTexture();

        // Every validation passes, so the chain kept both targets and the shared settings, and only the command buffer refuses.
        Assert.Throws<ObjectDisposedException>(() =>
        {
            CommandBuffer commandBuffer = default;
            new RenderPassBuilder(ref commandBuffer)
                .AddColorTarget(texture)
                .AddColorTarget(texture)
                .SetSharedColorTargetSettings(ColorTargetSettings.Clear)
                .Build();
        });
    }

    [Test]
    public void RenderPassBuilder_AddColorTargetsFromStackSpan_ReachesTheCommandBuffer()
    {
        Texture texture = CreateTexture();

        Assert.Throws<ObjectDisposedException>(() =>
        {
            CommandBuffer commandBuffer = default;
            ReadOnlySpan<Texture> textures = [texture, texture];
            new RenderPassBuilder(ref commandBuffer)
                .AddColorTargets(textures)
                .SetSharedColorTargetSettings(ColorTargetSettings.Clear)
                .Build();
        });
    }

    [Test]
    public void RenderPassBuilder_WithPerTargetSettingsMissingForOneTarget_ThrowsOnBuild()
    {
        Texture texture = CreateTexture();

        Assert.Throws<InvalidOperationException>(() =>
        {
            CommandBuffer commandBuffer = default;
            new RenderPassBuilder(ref commandBuffer)
                .AddColorTarget(texture, ColorTargetSettings.Clear)
                .AddColorTarget(texture)
                .Build();
        });
    }

    [Test]
    public void RenderPassBuilder_WithMoreThanEightColorTargets_Throws()
    {
        Texture texture = CreateTexture();

        Assert.Throws<InvalidOperationException>(() =>
        {
            CommandBuffer commandBuffer = default;
            RenderPassBuilder builder = new RenderPassBuilder(ref commandBuffer);
            for (int i = 0; i < 9; i++)
            {
                builder.AddColorTarget(texture);
            }
        });
    }

    private static Texture CreateTexture()
    {
        return (Texture)RuntimeHelpers.GetUninitializedObject(typeof(UserTexture));
    }
}
