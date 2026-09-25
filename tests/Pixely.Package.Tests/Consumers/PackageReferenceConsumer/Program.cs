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

    public override bool TryCreateRenderContext(Window window, [MaybeNullWhen(false)] out PackageRenderContext renderContext)
    {
        renderContext = default;
        return false;
    }
}

public ref struct PackageRenderContext : IRenderContext
{
    private CommandBuffer _commandBuffer;

    [UnscopedRef]
    public ref CommandBuffer CommandBuffer => ref _commandBuffer;

    public readonly Texture ColorTarget => null!;

    public void Dispose()
    {
    }
}

public ref struct PackageBasicRenderContext : IRenderContext
{
    private BasicRenderContext _basic;

    public PackageBasicRenderContext(SwapchainTexture swapchainTexture, CommandBuffer commandBuffer)
    {
        _basic = new BasicRenderContext(swapchainTexture, commandBuffer);
    }

    [UnscopedRef]
    public ref CommandBuffer CommandBuffer => ref _basic.CommandBuffer;

    public readonly Texture ColorTarget => _basic.ColorTarget;

    public void Dispose()
    {
        _basic.Dispose();
    }
}

public sealed class GeneratedService;
