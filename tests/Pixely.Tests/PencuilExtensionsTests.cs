using Pixely.App;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.Pencuil;
using Pixely.RenderOrchestration;

namespace Pixely.Tests;

public sealed class PencuilExtensionsTests
{
    [Test]
    public void UsePencuil_CustomRenderContext_RegistersRenderer()
    {
        PixelyAppBuilder services = new();

        services.UsePencuil<CustomRenderContext>(new ViewScope(1));

        Assert.Multiple(() =>
        {
            Assert.That(services.IsRegistered<IRenderer<CustomRenderContext>>(), Is.True);
            Assert.That(services.IsRegistered<VirtualFileSystem>(), Is.True);
        });
    }

    private sealed class CustomRenderContext : IRenderContext
    {
        public CommandBuffer CommandBuffer => null!;
        public Texture ColorTarget => null!;

        public void Dispose()
        {
        }
    }
}
