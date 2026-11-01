using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.TextureArray;

static partial class Program
{
    static void Configure(PixelyAppBuilder builder)
    {
        builder
            .UseDefaultContent()
            .ConfigureWindow(new WindowConfig(Size: (800, 600), Title: "Texture Array Demo"))
            .UseDefaultRendering();

        builder.AddSingleton<TextureArrayRenderer>(TextureArrayRenderer.Create);
        builder.AddAlias<IRenderer<BasicRenderContext>, TextureArrayRenderer>();
    }
}
