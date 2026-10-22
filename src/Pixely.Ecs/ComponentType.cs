namespace Pixely.Ecs;

internal static class ComponentTypes
{
    private static int _count;

    internal static int Next() => Interlocked.Increment(ref _count) - 1;
}

// Ids are process-wide, so archetypes of different worlds agree on them.
internal static class ComponentType<T> where T : struct
{
    internal static readonly int Id = ComponentTypes.Next();
}
