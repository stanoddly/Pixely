namespace Pixely.Ecs;

// The components of one entity that ComponentLookup<T1, ...> returns, in the order of its type arguments. C# cannot deconstruct into references,
// so they are fields.
public readonly ref struct ComponentRefs<T1, T2>
    where T1 : struct
    where T2 : struct
{
    public readonly ref T1 Item1;
    public readonly ref T2 Item2;

    internal ComponentRefs(ref T1 item1, ref T2 item2)
    {
        Item1 = ref item1;
        Item2 = ref item2;
    }
}

public readonly ref struct ComponentRefs<T1, T2, T3>
    where T1 : struct
    where T2 : struct
    where T3 : struct
{
    public readonly ref T1 Item1;
    public readonly ref T2 Item2;
    public readonly ref T3 Item3;

    internal ComponentRefs(ref T1 item1, ref T2 item2, ref T3 item3)
    {
        Item1 = ref item1;
        Item2 = ref item2;
        Item3 = ref item3;
    }
}

public readonly ref struct ComponentRefs<T1, T2, T3, T4>
    where T1 : struct
    where T2 : struct
    where T3 : struct
    where T4 : struct
{
    public readonly ref T1 Item1;
    public readonly ref T2 Item2;
    public readonly ref T3 Item3;
    public readonly ref T4 Item4;

    internal ComponentRefs(ref T1 item1, ref T2 item2, ref T3 item3, ref T4 item4)
    {
        Item1 = ref item1;
        Item2 = ref item2;
        Item3 = ref item3;
        Item4 = ref item4;
    }
}

public readonly ref struct ComponentRefs<T1, T2, T3, T4, T5>
    where T1 : struct
    where T2 : struct
    where T3 : struct
    where T4 : struct
    where T5 : struct
{
    public readonly ref T1 Item1;
    public readonly ref T2 Item2;
    public readonly ref T3 Item3;
    public readonly ref T4 Item4;
    public readonly ref T5 Item5;

    internal ComponentRefs(ref T1 item1, ref T2 item2, ref T3 item3, ref T4 item4, ref T5 item5)
    {
        Item1 = ref item1;
        Item2 = ref item2;
        Item3 = ref item3;
        Item4 = ref item4;
        Item5 = ref item5;
    }
}

public readonly ref struct ComponentRefs<T1, T2, T3, T4, T5, T6>
    where T1 : struct
    where T2 : struct
    where T3 : struct
    where T4 : struct
    where T5 : struct
    where T6 : struct
{
    public readonly ref T1 Item1;
    public readonly ref T2 Item2;
    public readonly ref T3 Item3;
    public readonly ref T4 Item4;
    public readonly ref T5 Item5;
    public readonly ref T6 Item6;

    internal ComponentRefs(ref T1 item1, ref T2 item2, ref T3 item3, ref T4 item4, ref T5 item5, ref T6 item6)
    {
        Item1 = ref item1;
        Item2 = ref item2;
        Item3 = ref item3;
        Item4 = ref item4;
        Item5 = ref item5;
        Item6 = ref item6;
    }
}

public readonly ref struct ComponentRefs<T1, T2, T3, T4, T5, T6, T7>
    where T1 : struct
    where T2 : struct
    where T3 : struct
    where T4 : struct
    where T5 : struct
    where T6 : struct
    where T7 : struct
{
    public readonly ref T1 Item1;
    public readonly ref T2 Item2;
    public readonly ref T3 Item3;
    public readonly ref T4 Item4;
    public readonly ref T5 Item5;
    public readonly ref T6 Item6;
    public readonly ref T7 Item7;

    internal ComponentRefs(ref T1 item1, ref T2 item2, ref T3 item3, ref T4 item4, ref T5 item5, ref T6 item6, ref T7 item7)
    {
        Item1 = ref item1;
        Item2 = ref item2;
        Item3 = ref item3;
        Item4 = ref item4;
        Item5 = ref item5;
        Item6 = ref item6;
        Item7 = ref item7;
    }
}

public readonly ref struct ComponentRefs<T1, T2, T3, T4, T5, T6, T7, T8>
    where T1 : struct
    where T2 : struct
    where T3 : struct
    where T4 : struct
    where T5 : struct
    where T6 : struct
    where T7 : struct
    where T8 : struct
{
    public readonly ref T1 Item1;
    public readonly ref T2 Item2;
    public readonly ref T3 Item3;
    public readonly ref T4 Item4;
    public readonly ref T5 Item5;
    public readonly ref T6 Item6;
    public readonly ref T7 Item7;
    public readonly ref T8 Item8;

    internal ComponentRefs(ref T1 item1, ref T2 item2, ref T3 item3, ref T4 item4, ref T5 item5, ref T6 item6, ref T7 item7, ref T8 item8)
    {
        Item1 = ref item1;
        Item2 = ref item2;
        Item3 = ref item3;
        Item4 = ref item4;
        Item5 = ref item5;
        Item6 = ref item6;
        Item7 = ref item7;
        Item8 = ref item8;
    }
}

// The components of one entity that ReadOnlyComponentLookup<T1, ...> returns, in the order of its type arguments. C# cannot deconstruct into references,
// so they are fields.
public readonly ref struct ReadOnlyComponentRefs<T1, T2>
    where T1 : struct
    where T2 : struct
{
    public readonly ref readonly T1 Item1;
    public readonly ref readonly T2 Item2;

    internal ReadOnlyComponentRefs(ref readonly T1 item1, ref readonly T2 item2)
    {
        Item1 = ref item1;
        Item2 = ref item2;
    }
}

public readonly ref struct ReadOnlyComponentRefs<T1, T2, T3>
    where T1 : struct
    where T2 : struct
    where T3 : struct
{
    public readonly ref readonly T1 Item1;
    public readonly ref readonly T2 Item2;
    public readonly ref readonly T3 Item3;

    internal ReadOnlyComponentRefs(ref readonly T1 item1, ref readonly T2 item2, ref readonly T3 item3)
    {
        Item1 = ref item1;
        Item2 = ref item2;
        Item3 = ref item3;
    }
}

public readonly ref struct ReadOnlyComponentRefs<T1, T2, T3, T4>
    where T1 : struct
    where T2 : struct
    where T3 : struct
    where T4 : struct
{
    public readonly ref readonly T1 Item1;
    public readonly ref readonly T2 Item2;
    public readonly ref readonly T3 Item3;
    public readonly ref readonly T4 Item4;

    internal ReadOnlyComponentRefs(ref readonly T1 item1, ref readonly T2 item2, ref readonly T3 item3, ref readonly T4 item4)
    {
        Item1 = ref item1;
        Item2 = ref item2;
        Item3 = ref item3;
        Item4 = ref item4;
    }
}

public readonly ref struct ReadOnlyComponentRefs<T1, T2, T3, T4, T5>
    where T1 : struct
    where T2 : struct
    where T3 : struct
    where T4 : struct
    where T5 : struct
{
    public readonly ref readonly T1 Item1;
    public readonly ref readonly T2 Item2;
    public readonly ref readonly T3 Item3;
    public readonly ref readonly T4 Item4;
    public readonly ref readonly T5 Item5;

    internal ReadOnlyComponentRefs(ref readonly T1 item1, ref readonly T2 item2, ref readonly T3 item3, ref readonly T4 item4, ref readonly T5 item5)
    {
        Item1 = ref item1;
        Item2 = ref item2;
        Item3 = ref item3;
        Item4 = ref item4;
        Item5 = ref item5;
    }
}

public readonly ref struct ReadOnlyComponentRefs<T1, T2, T3, T4, T5, T6>
    where T1 : struct
    where T2 : struct
    where T3 : struct
    where T4 : struct
    where T5 : struct
    where T6 : struct
{
    public readonly ref readonly T1 Item1;
    public readonly ref readonly T2 Item2;
    public readonly ref readonly T3 Item3;
    public readonly ref readonly T4 Item4;
    public readonly ref readonly T5 Item5;
    public readonly ref readonly T6 Item6;

    internal ReadOnlyComponentRefs(ref readonly T1 item1, ref readonly T2 item2, ref readonly T3 item3, ref readonly T4 item4, ref readonly T5 item5,
        ref readonly T6 item6)
    {
        Item1 = ref item1;
        Item2 = ref item2;
        Item3 = ref item3;
        Item4 = ref item4;
        Item5 = ref item5;
        Item6 = ref item6;
    }
}

public readonly ref struct ReadOnlyComponentRefs<T1, T2, T3, T4, T5, T6, T7>
    where T1 : struct
    where T2 : struct
    where T3 : struct
    where T4 : struct
    where T5 : struct
    where T6 : struct
    where T7 : struct
{
    public readonly ref readonly T1 Item1;
    public readonly ref readonly T2 Item2;
    public readonly ref readonly T3 Item3;
    public readonly ref readonly T4 Item4;
    public readonly ref readonly T5 Item5;
    public readonly ref readonly T6 Item6;
    public readonly ref readonly T7 Item7;

    internal ReadOnlyComponentRefs(ref readonly T1 item1, ref readonly T2 item2, ref readonly T3 item3, ref readonly T4 item4, ref readonly T5 item5,
        ref readonly T6 item6, ref readonly T7 item7)
    {
        Item1 = ref item1;
        Item2 = ref item2;
        Item3 = ref item3;
        Item4 = ref item4;
        Item5 = ref item5;
        Item6 = ref item6;
        Item7 = ref item7;
    }
}

public readonly ref struct ReadOnlyComponentRefs<T1, T2, T3, T4, T5, T6, T7, T8>
    where T1 : struct
    where T2 : struct
    where T3 : struct
    where T4 : struct
    where T5 : struct
    where T6 : struct
    where T7 : struct
    where T8 : struct
{
    public readonly ref readonly T1 Item1;
    public readonly ref readonly T2 Item2;
    public readonly ref readonly T3 Item3;
    public readonly ref readonly T4 Item4;
    public readonly ref readonly T5 Item5;
    public readonly ref readonly T6 Item6;
    public readonly ref readonly T7 Item7;
    public readonly ref readonly T8 Item8;

    internal ReadOnlyComponentRefs(ref readonly T1 item1, ref readonly T2 item2, ref readonly T3 item3, ref readonly T4 item4, ref readonly T5 item5,
        ref readonly T6 item6, ref readonly T7 item7, ref readonly T8 item8)
    {
        Item1 = ref item1;
        Item2 = ref item2;
        Item3 = ref item3;
        Item4 = ref item4;
        Item5 = ref item5;
        Item6 = ref item6;
        Item7 = ref item7;
        Item8 = ref item8;
    }
}
