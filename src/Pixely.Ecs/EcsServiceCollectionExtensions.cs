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

    // The Pixely.Ecs generator intercepts this call. It creates TSystem with the queries, lookups and writers its constructor takes from the SystemBuilder
    // and the rest from the provider, and adds it to the SystemScheduler. Systems that declare no order between them with IAfter<...> and IBefore<...> run in
    // the order of these calls.
    public static void AddSystem<TSystem>(this ServiceCollection services) where TSystem : class, ISystem
    {
        throw new InvalidOperationException(
            $"AddSystem<{typeof(TSystem).Name}>() was not intercepted by the source generator. Reference Pixely.Ecs.Generator as an analyzer and add Pixely.Ecs.Generated to InterceptorsNamespaces.");
    }
}
