using System.Diagnostics.CodeAnalysis;
using Pixely;
using Pixely.App;
using Pixely.DependencyInjection;
using Pixely.Gpu;
using Pixely.RenderOrchestration;

PixelyException exception = new("package runtime API");
if (exception.Message != "package runtime API" || SpriteFlip.Both != (SpriteFlip.Horizontal | SpriteFlip.Vertical))
{
    throw new InvalidOperationException("Packaged runtime APIs returned unexpected values.");
}

ServiceCollection services = new();
services.AddSingleton<GeneratedService>();
using ServiceProvider provider = services.BuildServiceProvider();
if (provider.GetRequiredService<GeneratedService>() is null)
{
    throw new InvalidOperationException("The generated dependency-injection registration failed.");
}

Console.WriteLine("Package consumer succeeded.");

public static class ManagedWindowApiConsumer
{
    public static void ConfigureManagedWindow(PixelyAppBuilder builder)
    {
        builder.AddWindow(new ViewScope(1), new WindowConfig(Title: "Package consumer"));
        builder.AddSingleton<PackageRenderContextProvider>(PackageRenderContextProvider.Create);
        builder.AddAlias<RenderContextProvider<PackageRenderContext>, PackageRenderContextProvider>();
        builder.UseWindowRendering<PackageRenderContext>(new ViewScope(1));
    }
}

public sealed class PackageRenderContextProvider : RenderContextProvider<PackageRenderContext>
{
    private PackageRenderContextProvider(GpuDevice gpuDevice)
    {
    }

    public static PackageRenderContextProvider Create(GpuDevice gpuDevice)
    {
        return new PackageRenderContextProvider(gpuDevice);
    }

    public override bool TryCreateRenderContext(Window window, [NotNullWhen(true)] out PackageRenderContext? renderContext)
    {
        renderContext = null;
        return false;
    }
}

public sealed class PackageRenderContext : IRenderContext
{
    public CommandBuffer CommandBuffer => null!;

    public Texture ColorTarget => null!;

    public void Dispose()
    {
    }
}

public sealed class PackageBasicRenderContext : BasicRenderContext
{
    public PackageBasicRenderContext(SwapchainTexture swapchainTexture, CommandBuffer commandBuffer) : base(swapchainTexture, commandBuffer)
    {
    }

    public PackageBasicRenderContext()
    {
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}

public sealed class PackageReusingRenderContextProvider : BasicRenderContextProvider<PackageBasicRenderContext>
{
    public PackageReusingRenderContextProvider(GpuDevice gpuDevice) : base(gpuDevice)
    {
    }

    protected override PackageBasicRenderContext CreateRenderContext()
    {
        return new PackageBasicRenderContext();
    }

    protected override void PrepareRenderContext(PackageBasicRenderContext renderContext, Window window)
    {
    }
}

public sealed class GeneratedService;
