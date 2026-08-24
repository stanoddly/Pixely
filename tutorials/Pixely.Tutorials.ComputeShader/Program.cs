using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.ComputeShader;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder appBuilder = new();
        appBuilder
            .UseDefaultContent()
            .UseDefaultRendering(
                new WindowConfig(Size: (800, 600), Title: "Compute Shader Demo"));

        appBuilder.AddSingleton<ComputeRenderer>(ComputeRenderer.Create);
        appBuilder.AddAlias<IRenderer<DefaultRenderContext>, ComputeRenderer>();

        using IPixelyApp pixelyApp = appBuilder.Build();
        return pixelyApp.Run();
    }
}
