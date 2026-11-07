using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Pixely.DependencyInjection;

namespace Pixely.Ecs;

public static partial class EcsServiceCollectionExtensions
{
    // The collections AddEcs registered a world in. IsRegistered also sees the parent's services, so it cannot tell a second call on the same collection
    // from a call on a child of a collection with a world.
    private static readonly ConditionalWeakTable<ServiceCollection, World> Worlds = new ConditionalWeakTable<ServiceCollection, World>();

    // Registers a World and the SystemScheduler that updates its systems every frame. Both live as long as the provider of this collection, so a stage
    // that calls AddEcs drops its entities when it unloads. A child collection that would share its parent's world throws, because its entities would
    // outlive it. A child collection that adds systems without calling AddEcs adds them to its parent's world.
    public static void AddEcs(this ServiceCollection services)
    {
        if (Worlds.TryGetValue(services, out _))
        {
            return;
        }

        if (services.IsRegistered<SystemScheduler>())
        {
            throw new InvalidOperationException(
                "A parent collection already has a World, so this collection would share it and its entities would outlive this collection's provider. Call AddEcs only in the collection whose provider owns the world, such as the stage that plays the game.");
        }

        World world = new World();
        SystemScheduler scheduler = new SystemScheduler(world);
        services.AddSingleton(world);
        services.AddSingleton(scheduler);
        services.OnDisposing((instance, _) => scheduler.Remove(instance));
        Worlds.Add(services, world);
    }

    // Registers the world's TQuery for code outside systems, such as a renderer or a view, which takes it in its constructor. A query that writes through a
    // Span<T> throws when the provider creates it, which for a singleton is when the provider is built, because only systems write the world.
    public static void AddQuery<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TQuery>(this ServiceCollection services) where TQuery : Query, new()
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

    // Adds unique to the world of this collection. Systems take it as Unique<T> or ReadOnlyUnique<TReadOnly>, and code outside systems as the
    // ReadOnlyUnique<TReadOnly> this registers. T itself is not registered, so only systems reach the type that writes. The world does not dispose it.
    // Only the collection that called AddEcs adds to its world, because a child's unique would outlive the child.
    public static void AddUnique<T, TReadOnly>(this ServiceCollection services, T unique) where T : class, TReadOnly where TReadOnly : class
    {
        World world = WorldForUnique(services, $"{typeof(T).Name}, {typeof(TReadOnly).Name}");
        world.AddUnique<T, TReadOnly>(unique);
        services.AddSingleton(new ReadOnlyUnique<TReadOnly>(world));
    }

    // Adds unique to the world of this collection for systems only, which take it as Unique<T>. It suits a unique that every system using it writes,
    // such as a random number generator, whose read-only type would have no members. It registers nothing, so code outside systems cannot reach it.
    public static void AddUnique<T>(this ServiceCollection services, T unique) where T : class => WorldForUnique(services, typeof(T).Name).AddUnique(unique);

    private static World WorldForUnique(ServiceCollection services, string typeArguments) => Worlds.TryGetValue(services, out World? world)
        ? world
        : throw new InvalidOperationException($"AddUnique<{typeArguments}> adds to the world of its collection, and this collection did not call AddEcs. Call it on the collection that did, such as the stage that plays the game.");

    // Registers a lookup of T for code outside systems, which takes it in its constructor. It reads at any time, unlike a system's lookup.
    public static void AddReadOnlyComponentLookup<T>(this ServiceCollection services) where T : struct =>
        services.AddSingleton(static provider => new ReadOnlyComponentLookup<T>(provider.GetRequiredService<World>()));

    // The Pixely.Ecs generator intercepts this call. It creates TSystem with the queries, lookups and writers its constructor takes from the SystemBuilder
    // and the rest from the provider, and adds it to the SystemScheduler. Systems that declare no order between them with IAfter<...> and IBefore<...> run in
    // the order of these calls.
    public static void AddSystem<TSystem>(this ServiceCollection services) where TSystem : class, ISystem
    {
        throw new InvalidOperationException(
            $"AddSystem<{typeof(TSystem).Name}>() was not intercepted by the source generator. Reference Pixely.Ecs.Generator as an analyzer and add Pixely.Ecs.Generated to InterceptorsNamespaces.");
    }
}
