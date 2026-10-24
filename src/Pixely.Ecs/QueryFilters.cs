namespace Pixely.Ecs;

// A query class requires the type arguments of IWith<...> and excludes those of IWithout<...>, in addition to the components of its spans.
// The members are internal, so only the generic interfaces here implement them, and a query implements at most one of each.
public interface IWith
{
    internal void DescribeWith(QueryDescription description);
}

public interface IWithout
{
    internal void DescribeWithout(QueryDescription description);
}

public interface IWith<T1> : IWith where T1 : struct
{
    void IWith.DescribeWith(QueryDescription description)
    {
        description.With<T1>();
    }
}

public interface IWith<T1, T2> : IWith where T1 : struct where T2 : struct
{
    void IWith.DescribeWith(QueryDescription description)
    {
        description.With<T1>();
        description.With<T2>();
    }
}

public interface IWith<T1, T2, T3> : IWith where T1 : struct where T2 : struct where T3 : struct
{
    void IWith.DescribeWith(QueryDescription description)
    {
        description.With<T1>();
        description.With<T2>();
        description.With<T3>();
    }
}

public interface IWith<T1, T2, T3, T4> : IWith where T1 : struct where T2 : struct where T3 : struct where T4 : struct
{
    void IWith.DescribeWith(QueryDescription description)
    {
        description.With<T1>();
        description.With<T2>();
        description.With<T3>();
        description.With<T4>();
    }
}

public interface IWith<T1, T2, T3, T4, T5> : IWith where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
{
    void IWith.DescribeWith(QueryDescription description)
    {
        description.With<T1>();
        description.With<T2>();
        description.With<T3>();
        description.With<T4>();
        description.With<T5>();
    }
}

public interface IWith<T1, T2, T3, T4, T5, T6> : IWith where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct
{
    void IWith.DescribeWith(QueryDescription description)
    {
        description.With<T1>();
        description.With<T2>();
        description.With<T3>();
        description.With<T4>();
        description.With<T5>();
        description.With<T6>();
    }
}

public interface IWith<T1, T2, T3, T4, T5, T6, T7> : IWith where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct
{
    void IWith.DescribeWith(QueryDescription description)
    {
        description.With<T1>();
        description.With<T2>();
        description.With<T3>();
        description.With<T4>();
        description.With<T5>();
        description.With<T6>();
        description.With<T7>();
    }
}

public interface IWith<T1, T2, T3, T4, T5, T6, T7, T8> : IWith where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct
{
    void IWith.DescribeWith(QueryDescription description)
    {
        description.With<T1>();
        description.With<T2>();
        description.With<T3>();
        description.With<T4>();
        description.With<T5>();
        description.With<T6>();
        description.With<T7>();
        description.With<T8>();
    }
}

public interface IWithout<T1> : IWithout where T1 : struct
{
    void IWithout.DescribeWithout(QueryDescription description)
    {
        description.Without<T1>();
    }
}

public interface IWithout<T1, T2> : IWithout where T1 : struct where T2 : struct
{
    void IWithout.DescribeWithout(QueryDescription description)
    {
        description.Without<T1>();
        description.Without<T2>();
    }
}

public interface IWithout<T1, T2, T3> : IWithout where T1 : struct where T2 : struct where T3 : struct
{
    void IWithout.DescribeWithout(QueryDescription description)
    {
        description.Without<T1>();
        description.Without<T2>();
        description.Without<T3>();
    }
}

public interface IWithout<T1, T2, T3, T4> : IWithout where T1 : struct where T2 : struct where T3 : struct where T4 : struct
{
    void IWithout.DescribeWithout(QueryDescription description)
    {
        description.Without<T1>();
        description.Without<T2>();
        description.Without<T3>();
        description.Without<T4>();
    }
}

public interface IWithout<T1, T2, T3, T4, T5> : IWithout where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
{
    void IWithout.DescribeWithout(QueryDescription description)
    {
        description.Without<T1>();
        description.Without<T2>();
        description.Without<T3>();
        description.Without<T4>();
        description.Without<T5>();
    }
}

public interface IWithout<T1, T2, T3, T4, T5, T6> : IWithout where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct
{
    void IWithout.DescribeWithout(QueryDescription description)
    {
        description.Without<T1>();
        description.Without<T2>();
        description.Without<T3>();
        description.Without<T4>();
        description.Without<T5>();
        description.Without<T6>();
    }
}

public interface IWithout<T1, T2, T3, T4, T5, T6, T7> : IWithout where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct
{
    void IWithout.DescribeWithout(QueryDescription description)
    {
        description.Without<T1>();
        description.Without<T2>();
        description.Without<T3>();
        description.Without<T4>();
        description.Without<T5>();
        description.Without<T6>();
        description.Without<T7>();
    }
}

public interface IWithout<T1, T2, T3, T4, T5, T6, T7, T8> : IWithout where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct
{
    void IWithout.DescribeWithout(QueryDescription description)
    {
        description.Without<T1>();
        description.Without<T2>();
        description.Without<T3>();
        description.Without<T4>();
        description.Without<T5>();
        description.Without<T6>();
        description.Without<T7>();
        description.Without<T8>();
    }
}
