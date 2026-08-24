using System.Diagnostics.CodeAnalysis;

namespace Pixely.RenderOrchestration;

public interface IRenderContextProvider<TRenderContext>
    where TRenderContext : IRenderContext
{
    bool TryCreateRenderContext(Window window, [NotNullWhen(true)] out TRenderContext? renderContext);
}
