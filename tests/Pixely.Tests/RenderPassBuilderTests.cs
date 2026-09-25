using System.Runtime.CompilerServices;
using Pixely.Gpu;

namespace Pixely.Tests;

public class RenderPassBuilderTests
{
    [Test]
    public void Build_WithoutCommandBuffer_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            RenderPassBuilder builder = default;
            builder.Build();
        });
    }

    [Test]
    public void Build_WithoutTargets_Throws()
    {
        CommandBuffer commandBuffer = CreateSubmittedCommandBuffer();

        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(() => new RenderPassBuilder(commandBuffer).Build());
        Assert.That(exception!.Message, Does.Contain("At least one color target"));
    }

    [Test]
    public void Build_WithChainedTargets_ReachesTheCommandBuffer()
    {
        CommandBuffer commandBuffer = CreateSubmittedCommandBuffer();
        Texture texture = CreateTexture();

        // Every validation passes, so the chain kept both targets and the shared settings, and only the command buffer refuses.
        Assert.Throws<ObjectDisposedException>(() => new RenderPassBuilder(commandBuffer)
            .AddColorTarget(texture)
            .AddColorTarget(texture)
            .SetSharedColorTargetSettings(ColorTargetSettings.Clear)
            .Build());
    }

    [Test]
    public void AddColorTargets_FromStackSpan_ReachesTheCommandBuffer()
    {
        CommandBuffer commandBuffer = CreateSubmittedCommandBuffer();
        Texture texture = CreateTexture();

        Assert.Throws<ObjectDisposedException>(() =>
        {
            ReadOnlySpan<Texture> textures = [texture, texture];
            new RenderPassBuilder(commandBuffer)
                .AddColorTargets(textures)
                .SetSharedColorTargetSettings(ColorTargetSettings.Clear)
                .Build();
        });
    }

    [Test]
    public void Build_WithPerTargetSettingsMissingForOneTarget_Throws()
    {
        CommandBuffer commandBuffer = CreateSubmittedCommandBuffer();
        Texture texture = CreateTexture();

        Assert.Throws<InvalidOperationException>(() => new RenderPassBuilder(commandBuffer)
            .AddColorTarget(texture, ColorTargetSettings.Clear)
            .AddColorTarget(texture)
            .Build());
    }

    [Test]
    public void AddColorTarget_BeyondEightTargets_Throws()
    {
        CommandBuffer commandBuffer = CreateSubmittedCommandBuffer();
        Texture texture = CreateTexture();

        Assert.Throws<InvalidOperationException>(() =>
        {
            RenderPassBuilder builder = new RenderPassBuilder(commandBuffer);
            for (int i = 0; i < 9; i++)
            {
                builder.AddColorTarget(texture);
            }
        });
    }

    // A command buffer that records nothing, so a build that gets past its own checks stops at the command buffer, before SDL.
    private static CommandBuffer CreateSubmittedCommandBuffer()
    {
        return new CommandBuffer((GpuDevice)RuntimeHelpers.GetUninitializedObject(typeof(GpuDevice)));
    }

    private static Texture CreateTexture()
    {
        return (Texture)RuntimeHelpers.GetUninitializedObject(typeof(UserTexture));
    }
}
