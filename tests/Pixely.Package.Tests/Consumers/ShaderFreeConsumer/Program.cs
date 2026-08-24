using Pixely;
using Pixely.App;
using Pixely.DependencyInjection;

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
    public static Window ResolveManagedWindow(ServiceProvider provider)
    {
        return provider.GetWindow(new ViewScope(1));
    }

    public static void ConfigureManagedWindow(PixelyAppBuilder builder)
    {
        builder.AddWindow(new ViewScope(1), new WindowConfig(Title: "Package consumer"));
    }
}

public sealed class GeneratedService;
