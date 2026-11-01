using Pixely.App;
using Pixely.Models;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.SkinnedModel;

static partial class Program
{
    static void Configure(PixelyAppBuilder builder)
    {
        builder
            .UseDefaultContent()
            .ConfigureWindow(new WindowConfig(Size: (800, 600), Title: "Skinned model"))
            .UseDefaultRendering();

        builder.RegisterModelLoading();
        builder.AddSingleton<AnimatedModel>(AnimatedModel.Create);
        builder.AddAlias<IUpdatable, AnimatedModel>();
        builder.AddSingleton<IRenderer<BasicRenderContext>>(ModelRenderer.Create);
    }
}
