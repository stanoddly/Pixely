namespace Pixely.Ecs.Tests;

// Systems whose Update runs a delegate, for tests that need a system with a given contract. A system with type arguments is added through
// the generated code of SystemScheduler.Add, like any other ISystem<...>.
internal sealed class ActionSystem(Action update) : ISystem
{
    public void Update() => update();
}

internal sealed class ActionSystem<T1>(Action<T1> update) : ISystem<T1>
{
    public void Update(T1 item1) => update(item1);
}

internal sealed class ActionSystem<T1, T2>(Action<T1, T2> update) : ISystem<T1, T2>
{
    public void Update(T1 item1, T2 item2) => update(item1, item2);
}

internal sealed class ActionSystem<T1, T2, T3>(Action<T1, T2, T3> update) : ISystem<T1, T2, T3>
{
    public void Update(T1 item1, T2 item2, T3 item3) => update(item1, item2, item3);
}
