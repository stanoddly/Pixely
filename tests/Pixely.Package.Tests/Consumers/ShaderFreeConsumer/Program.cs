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
    public static void ConfigureManagedWindow(PixelyAppBuilder appBuilder)
    {
        appBuilder.AddWindow(new ViewScope(1), new WindowConfig(Title: "Package consumer"));
        appBuilder.AddSingleton<PackageRenderContextProvider>(PackageRenderContextProvider.Create);
        appBuilder.AddAlias<IRenderContextProvider<PackageRenderContext>, PackageRenderContextProvider>();
        appBuilder.UseWindowRendering<PackageRenderContext>(new ViewScope(1));
    }
}

public sealed class PackageRenderContextProvider : IRenderContextProvider<PackageRenderContext>
{
    private PackageRenderContextProvider(GpuDevice gpuDevice)
    {
    }

    public static PackageRenderContextProvider Create(GpuDevice gpuDevice)
    {
        return new PackageRenderContextProvider(gpuDevice);
    }

    public bool TryCreateRenderContext(Window window, [NotNullWhen(true)] out PackageRenderContext? renderContext)
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

public sealed class GeneratedService;
