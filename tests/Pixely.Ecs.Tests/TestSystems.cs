namespace Pixely.Ecs.Tests;

// A system whose Update runs a delegate, for tests that need a system with a given contract. Of takes the queries, lookups and writers that a
// SystemScheduler.Add function creates through its builder and passes them to the delegate.
internal sealed class ActionSystem(Action update) : ISystem
{
    public void Update() => update();

    public static ActionSystem Of<T1>(T1 item1, Action<T1> update) => new ActionSystem(() => update(item1));

    public static ActionSystem Of<T1, T2>(T1 item1, T2 item2, Action<T1, T2> update) => new ActionSystem(() => update(item1, item2));

    public static ActionSystem Of<T1, T2, T3>(T1 item1, T2 item2, T3 item3, Action<T1, T2, T3> update) => new ActionSystem(() => update(item1, item2, item3));
}

internal static class SchedulerExtensions
{
    // Updates the scheduler once with a system that passes what create takes from its builder to update, and removes that system, so a test changes
    // the world as a game does. The scheduler's other systems run in that update too.
    public static TResult RunOnce<TContract, TResult>(this SystemScheduler scheduler, Func<SystemBuilder, TContract> create, Func<TContract, TResult> update)
    {
        TResult result = default!;
        ActionSystem system = scheduler.Add(builder => ActionSystem.Of(create(builder), contract => result = update(contract)));
        try
        {
            scheduler.Update();
        }
        finally
        {
            scheduler.Remove(system);
        }

        return result;
    }

    public static void RunOnce<TContract>(this SystemScheduler scheduler, Func<SystemBuilder, TContract> create, Action<TContract> update) =>
        scheduler.RunOnce(create, contract =>
        {
            update(contract);
            return true;
        });
}
