using Pixely.Collections;

namespace Pixely.Ecs;

internal abstract class Column
{
    public abstract Column CreateEmpty();

    public abstract void Grow(int count);

    // Appends the row to the destination, which holds the same component type, and swap-removes it here.
    public abstract void MoveRow(int row, Column destination);

    public abstract void RemoveRow(int row);
}

internal sealed class Column<T> : Column where T : struct
{
    public FastList<T> Values { get; } = new FastList<T>();

    public override Column CreateEmpty() => new Column<T>();

    public override void Grow(int count) => Values.Resize(Values.Length + count);

    public override void MoveRow(int row, Column destination)
    {
        ((Column<T>)destination).Values.Add(Values[row]);
        Values.SwapRemove(row);
    }

    public override void RemoveRow(int row) => Values.SwapRemove(row);
}
