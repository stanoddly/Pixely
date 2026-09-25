namespace Pixely.RenderOrchestration;

public interface IRenderer<TRenderContext>
    where TRenderContext : allows ref struct
{
    /// <summary>
    /// The order the renderer draws in relative to the other renderers. Lower numbers draw first.
    /// </summary>
    int RenderOrder => 0;

    ViewScope ViewScope => default;
    void Render(ref TRenderContext renderContext);
}

public class NullRenderer<TRenderContext> : IRenderer<TRenderContext>
    where TRenderContext : allows ref struct
{
    public void Render(ref TRenderContext renderContext)
    {
    }
}
