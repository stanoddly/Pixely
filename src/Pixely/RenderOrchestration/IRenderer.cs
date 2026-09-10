namespace Pixely.RenderOrchestration;

public interface IRenderer<in TRenderContext>
{
    /// <summary>
    /// The order the renderer draws in relative to the other renderers. Lower numbers draw first.
    /// </summary>
    int RenderOrder => 0;

    ViewScope ViewScope => default;
    void Render(TRenderContext renderContext);
}

public class NullRenderer<TRenderContext> : IRenderer<TRenderContext>
{
    public void Render(TRenderContext renderContext)
    {
    }
}
