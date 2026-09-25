using Pixely.Gpu;
using Pixely.RenderOrchestration;

namespace Pixely.Tests;

public class BasicRenderContextTests
{
    [Test]
    public void CommandBuffer_BeforeTheFirstFrame_Throws()
    {
        GameRenderContext renderContext = new();

        Assert.Throws<ObjectDisposedException>(() => _ = renderContext.CommandBuffer);
    }

    [Test]
    public void SwapchainTexture_BeforeTheFirstFrame_Throws()
    {
        GameRenderContext renderContext = new();

        Assert.Throws<ObjectDisposedException>(() => _ = renderContext.SwapchainTexture);
    }

    [Test]
    public void Dispose_WithoutAFrame_DoesNothing()
    {
        GameRenderContext renderContext = new();

        Assert.DoesNotThrow(renderContext.Dispose);
    }

    // A game's context and provider, written against the protected members only.
    private sealed class GameRenderContext : BasicRenderContext
    {
        public ShortSize RenderSize { get; set; }
    }

    private sealed class GameRenderContextProvider(GpuDevice gpuDevice) : BasicRenderContextProvider<GameRenderContext>(gpuDevice)
    {
        protected override GameRenderContext CreateRenderContext()
        {
            return new GameRenderContext();
        }

        protected override void PrepareRenderContext(GameRenderContext renderContext, Window window)
        {
            renderContext.RenderSize = window.RenderSizeInPixels;
        }
    }
}
