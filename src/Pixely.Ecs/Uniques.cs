using System.Diagnostics.CodeAnalysis;

namespace Pixely.Ecs;

// The world's unique T for a system, which reads and writes it only during its Update. T is a class, so a system that keeps Value past Update can still
// write it, which nothing checks.
public sealed class Unique<T> : IUpdateScoped where T : class
{
    private readonly T _value;
    private bool _isAvailable;

    internal Unique(CommandBuffer commands)
    {
        _value = commands.World.GetUnique<T>();
        commands.AddUpdateScoped(this);
    }

    public T Value => _isAvailable ? _value : Uniques.ThrowOutsideUpdate<T>();

    void IUpdateScoped.SetWorld(World? world) => _isAvailable = world is not null;
}

// The world's unique through its read-only type. A system reads it only during its Update. The one AddUnique registers for code outside systems
// reads at any time.
public sealed class ReadOnlyUnique<TReadOnly> : IUpdateScoped where TReadOnly : class
{
    private readonly TReadOnly _value;
    private bool _isAvailable;

    internal ReadOnlyUnique(CommandBuffer commands)
    {
        _value = commands.World.GetReadOnlyUnique<TReadOnly>();
        commands.AddUpdateScoped(this);
    }

    internal ReadOnlyUnique(World world)
    {
        _value = world.GetReadOnlyUnique<TReadOnly>();
        _isAvailable = true;
    }

    public TReadOnly Value => _isAvailable ? _value : Uniques.ThrowOutsideUpdate<TReadOnly>();

    void IUpdateScoped.SetWorld(World? world) => _isAvailable = world is not null;
}

internal static class Uniques
{
    [DoesNotReturn]
    public static T ThrowOutsideUpdate<T>() => throw new InvalidOperationException("A system reads its uniques only during its Update.");
}
