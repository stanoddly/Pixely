using Pixely.DependencyInjection;

namespace Pixely.Ecs;

public static partial class EcsServiceCollectionExtensions
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

    // Registers the world's TQuery for code outside systems, such as a renderer or a view, which takes it in its constructor. A query that writes through a
    // Span<T> throws when the provider creates it, which for a singleton is when the provider is built, because only systems write the world.
    public static void AddQuery<TQuery>(this ServiceCollection services) where TQuery : Query, new()
    {
        services.AddSingleton<TQuery>(static provider =>
        {
            TQuery query = provider.GetRequiredService<World>().Query<TQuery>();
            if (query.State.Writes)
            {
                throw new InvalidOperationException($"The query {typeof(TQuery).Name} writes through a Span<T>, and only systems write the world. Use ReadOnlySpan<T> for code outside systems.");
            }

            return query;
        });
    }

    // Registers a lookup of T for code outside systems, which takes it in its constructor. It reads at any time, unlike a system's lookup.
    public static void AddReadOnlyComponentLookup<T>(this ServiceCollection services) where T : struct =>
        services.AddSingleton(static provider => new ReadOnlyComponentLookup<T>(provider.GetRequiredService<World>()));

    // Registers the sources of the relationship T for code outside systems, which takes them in its constructor.
    public static void AddRelationshipSources<T>(this ServiceCollection services) where T : struct, IRelationship =>
        services.AddSingleton(static provider => new RelationshipSources<T>(provider.GetRequiredService<World>()));

    // The Pixely.Ecs generator intercepts this call. It creates TSystem with the queries, lookups and writers its constructor takes from the SystemBuilder
    // and the rest from the provider, and adds it to the SystemScheduler. Systems that declare no order between them with IAfter<...> and IBefore<...> run in
    // the order of these calls.
    public static void AddSystem<TSystem>(this ServiceCollection services) where TSystem : class, ISystem
    {
        throw new InvalidOperationException(
            $"AddSystem<{typeof(TSystem).Name}>() was not intercepted by the source generator. Reference Pixely.Ecs.Generator as an analyzer and add Pixely.Ecs.Generated to InterceptorsNamespaces.");
    }
}
