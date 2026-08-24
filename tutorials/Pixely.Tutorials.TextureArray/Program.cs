using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.TextureArray;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder builder = new();
        builder
            .AddContentFromProjectDirectory("Content")
            .UseDefaultRendering(
                new WindowConfig(Size: (800, 600), Title: "Texture Array Demo"));

        builder.AddSingleton<TextureArrayRenderer>(TextureArrayRenderer.Create);
        builder.AddAlias<IRenderer<DefaultRenderContext>, TextureArrayRenderer>();

        using IPixelyApp pixelyApp = builder.Build();
        return pixelyApp.Run();
    }
}
