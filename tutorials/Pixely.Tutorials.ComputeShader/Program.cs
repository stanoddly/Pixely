using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.ComputeShader;

static partial class Program
{
    static void Configure(PixelyAppBuilder builder)
    {
        builder
            .UseDefaultContent()
            .ConfigureWindow(new WindowConfig(Size: (800, 600), Title: "Compute Shader Demo"))
            .UseDefaultRendering();

        builder.AddSingleton<ComputeRenderer>(ComputeRenderer.Create);
        builder.AddAlias<IRenderer<BasicRenderContext>, ComputeRenderer>();
    }
}
