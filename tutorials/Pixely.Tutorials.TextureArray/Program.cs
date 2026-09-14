using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.TextureArray;

static partial class Program
{
    static void Configure(PixelyAppBuilder builder, string[] args)
    {
        builder
            .UseDefaultContent()
            .UseDefaultRendering(
                new WindowConfig(Size: (800, 600), Title: "Texture Array Demo"));

        builder.AddSingleton<TextureArrayRenderer>(TextureArrayRenderer.Create);
        builder.AddAlias<IRenderer<BasicRenderContext>, TextureArrayRenderer>();
    }
}
