using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.ImageLoading;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder builder = new();
        builder
            .AddContentFromProjectDirectory("Content")
            .UseDefaultRendering(
                new WindowConfig(Size: (443, 410), Title: "Image Loading Demo"));

        builder.AddSingleton<ImageLoadingRenderer>(ImageLoadingRenderer.Create);
        builder.AddAlias<IRenderer<DefaultRenderContext>, ImageLoadingRenderer>();

        using IPixelyApp pixelyApp = builder.Build();
        return pixelyApp.Run();
    }
}
