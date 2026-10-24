using Pixely.DependencyInjection;

namespace Pixely.Ecs;

public static class EcsServiceCollectionExtensions
{
    // Registers a World and the SystemScheduler that updates its systems every frame. A child collection keeps its parent's.
    public static void AddEcs(this ServiceCollection services)
    {
        if (services.IsRegistered<SystemScheduler>())
        {
            return;
        }

        World world = new World();
        SystemScheduler scheduler = new SystemScheduler(world);
        services.AddSingleton(world);
        services.AddSingleton(scheduler);
        services.OnDisposing((instance, _) => scheduler.Remove(instance));
    }

    // The Pixely.Ecs generator intercepts this call. It creates TSystem, an ISystem or ISystem<...>, with services for its constructor, and adds it to the
    // SystemScheduler with the queries and writers of its ISystem<...>. Systems run in the order of these calls.
    public static void AddSystem<TSystem>(this ServiceCollection services) where TSystem : class
    {
        throw new InvalidOperationException(
            $"AddSystem<{typeof(TSystem).Name}>() was not intercepted by the source generator. Reference Pixely.Ecs.Generator as an analyzer and add Pixely.Ecs.Generated to InterceptorsNamespaces.");
    }
}
