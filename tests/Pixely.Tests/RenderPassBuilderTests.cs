using Pixely.Gpu;

namespace Pixely.Tests;

public class RenderPassBuilderTests
{
    // The builder validates before it touches the command buffer, so these cases need no GPU device.
    private static RenderPassBuilder CreateBuilder()
    {
        return new RenderPassBuilder(null!);
    }

    private sealed class FakeTexture : Texture
    {
        public FakeTexture() : base(default, new ShortSize(1, 1), TextureFormat.R8G8B8A8Unorm, 4)
        {
        }

        public override void Dispose()
        {
        }
    }

    [Test]
    public void Build_WithSharedAndPerTargetSettings_Throws()
    {
        RenderPassBuilder builder = CreateBuilder()
            .AddColorTarget(new FakeTexture(), ColorTargetSettings.Clear)
            .SetSharedColorTargetSettings(ColorTargetSettings.Clear);

        Assert.That(() => builder.Build(), Throws.InvalidOperationException);
    }

    [Test]
    public void Build_WithColorTargetAndNoSettings_Throws()
    {
        RenderPassBuilder builder = CreateBuilder().AddColorTarget(new FakeTexture());

        Assert.That(() => builder.Build(), Throws.InvalidOperationException);
    }

    [Test]
    public void Build_WithoutColorTargetsOrDepthBuffer_Throws()
    {
        RenderPassBuilder builder = CreateBuilder();

        Assert.That(() => builder.Build(), Throws.InvalidOperationException);
    }

    [Test]
    public void Build_WithFewerPerTargetSettingsThanColorTargets_Throws()
    {
        RenderPassBuilder builder = CreateBuilder()
            .AddColorTarget(new FakeTexture(), ColorTargetSettings.Clear)
            .AddColorTarget(new FakeTexture());

        Assert.That(() => builder.Build(), Throws.InvalidOperationException);
    }

    [Test]
    public void AddColorTarget_BeyondMaxColorTargets_Throws()
    {
        RenderPassBuilder builder = CreateBuilder();
        for (int target = 0; target < CommandBuffer.MaxColorTargets; target++)
        {
            builder = builder.AddColorTarget(new FakeTexture());
        }

        Assert.That(() => builder.AddColorTarget(new FakeTexture()), Throws.InvalidOperationException);
    }

    [Test]
    public void AddColorTargets_BeyondMaxColorTargets_Throws()
    {
        Texture[] textures = new Texture[CommandBuffer.MaxColorTargets + 1];
        for (int target = 0; target < textures.Length; target++)
        {
            textures[target] = new FakeTexture();
        }

        RenderPassBuilder builder = CreateBuilder();

        Assert.That(() => builder.AddColorTargets(textures), Throws.InvalidOperationException);
    }

    [Test]
    public void ConfiguringABuilder_LeavesTheReceiverAlone()
    {
        RenderPassBuilder shared = CreateBuilder().SetSharedColorTargetSettings(ColorTargetSettings.Clear);

        // Two passes branching off the same configuration, as documented in docs/render-pass-flow.md.
        shared.AddColorTarget(new FakeTexture());
        shared.AddColorTarget(new FakeTexture());

        // Neither branch may have added to shared, so it still has room for every color target.
        RenderPassBuilder filled = shared;
        for (int target = 0; target < CommandBuffer.MaxColorTargets; target++)
        {
            filled = filled.AddColorTarget(new FakeTexture());
        }

        Assert.That(() => filled.AddColorTarget(new FakeTexture()), Throws.InvalidOperationException);
    }

    [Test]
    public void AddColorTargets_BeyondCapacity_AddsNothing()
    {
        Texture[] textures = [new FakeTexture(), new FakeTexture()];

        RenderPassBuilder builder = CreateBuilder();
        for (int target = 0; target < CommandBuffer.MaxColorTargets - 1; target++)
        {
            builder = builder.AddColorTarget(new FakeTexture());
        }

        Assert.Multiple(() =>
        {
            Assert.That(() => builder.AddColorTargets(textures), Throws.InvalidOperationException);
            // The rejected pair must not have consumed the one remaining slot.
            Assert.That(() => builder.AddColorTarget(new FakeTexture()), Throws.Nothing);
        });
    }

    [Test]
    public void Copies_DoNotShareState()
    {
        RenderPassBuilder original = CreateBuilder();

        RenderPassBuilder copy = original;
        for (int target = 0; target < CommandBuffer.MaxColorTargets; target++)
        {
            copy = copy.AddColorTarget(new FakeTexture());
        }

        Assert.Multiple(() =>
        {
            Assert.That(() => copy.AddColorTarget(new FakeTexture()), Throws.InvalidOperationException);
            Assert.That(() => original.AddColorTarget(new FakeTexture()), Throws.Nothing);
        });
    }

    [Test]
    public void DescribingAPass_DoesNotAllocate()
    {
        Texture colorTarget = new FakeTexture();
        Texture depthBuffer = new FakeTexture();

        for (int warmUp = 0; warmUp < 4; warmUp++)
        {
            Describe(colorTarget, depthBuffer);
        }

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 16; iteration++)
        {
            Describe(colorTarget, depthBuffer);
        }

        long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

        Assert.That(allocatedAfter - allocatedBefore, Is.Zero);
    }

    // Everything a renderer does per frame up to Build, which needs a real command buffer.
    private static RenderPassBuilder Describe(Texture colorTarget, Texture depthBuffer)
    {
        return new RenderPassBuilder(null!)
            .AddColorTarget(colorTarget)
            .SetSharedColorTargetSettings(ColorTargetSettings.Clear)
            .SetDepthBuffer(depthBuffer, DepthBufferSettings.Default);
    }
}
