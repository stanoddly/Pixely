using Pixely;
using Pixely.DependencyInjection;
using Pixely.Ecs;
using System.Reflection;

string[] runtimeAssemblies =
[
    "Pixely",
    "Pixely.PathFinding",
    "Pixely.Fitness",
    "Pixely.Audio",
    "Pixely.Collections",
    "Pixely.Componentize",
    "Pixely.Core",
    "Pixely.DependencyInjection",
    "Pixely.Ecs",
    "Pixely.Events",
    "Pixely.Logging",
    "Pixely.Messages",
    "Pixely.Models",
    "Pixely.ShaderCommon",
    "Pixely.Utils"
];
foreach (string runtimeAssembly in runtimeAssemblies)
{
    Assembly.Load(new AssemblyName(runtimeAssembly)).GetTypes();
}

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

// AddSystem<T> is intercepted, so this needs the packaged ECS generator and the SDK's InterceptorsNamespaces.
ServiceCollection ecsServices = new();
ecsServices.AddEcs();
ecsServices.AddSystem<CountingSystem>();
using ServiceProvider ecsProvider = ecsServices.BuildServiceProvider();
SystemScheduler scheduler = ecsProvider.GetRequiredService<SystemScheduler>();
scheduler.Update();
scheduler.Update();
if (CountingSystem.Counted != 1)
{
    throw new InvalidOperationException($"The generated ECS system counted {CountingSystem.Counted} entities instead of 1.");
}

Console.WriteLine("Package consumer succeeded.");

public sealed class GeneratedService;

public record struct Counter(int Value);

public sealed class CountingSystem(Query<ReadOnlySpan<Counter>> counters, Spawner<Counter> spawner) : ISystem
{
    public static int Counted;

    public void Update()
    {
        Counted = 0;
        foreach (Columns<ReadOnlySpan<Counter>> columns in counters)
        {
            Counted += columns.Count;
        }
        if (Counted == 0)
        {
            spawner.Spawn(new Counter(1));
        }
    }
}
