using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.ComputeShader;

static partial class Program
{
    static partial void Configure(PixelyAppBuilder builder, string[] args)
    {
        builder
            .UseDefaultContent()
            .UseDefaultRendering(
                new WindowConfig(Size: (800, 600), Title: "Compute Shader Demo"));

        builder.AddSingleton<ComputeRenderer>(ComputeRenderer.Create);
        builder.AddAlias<IRenderer<BasicRenderContext>, ComputeRenderer>();
    }
}
