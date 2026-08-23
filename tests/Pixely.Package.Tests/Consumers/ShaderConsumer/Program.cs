using Pixely;
using Pixely.DependencyInjection;
using System.Reflection;

string[] runtimeAssemblies =
[
    "Pixely",
    "Pixely.PathFinding",
    "Pixely.Architecture",
    "Pixely.Architecture.Testing",
    "Pixely.Audio",
    "Pixely.Collections",
    "Pixely.Componentize",
    "Pixely.Core",
    "Pixely.DependencyInjection",
    "Pixely.Events",
    "Pixely.Logging",
    "Pixely.Pencuil",
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

Console.WriteLine("Package consumer succeeded.");

public sealed class GeneratedService;
