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
}
