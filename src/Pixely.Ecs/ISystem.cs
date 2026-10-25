namespace Pixely.Ecs;

// Updated by a SystemScheduler once per update. A system without queries or writers implements it; its constructor takes services.
public interface ISystem
{
    void Update();
}

// Updated by a SystemScheduler once per update, with the queries, lookups and writers its type arguments name. They are the system's contract;
// its constructor takes only services.
public interface ISystem<T1>
    where T1 : allows ref struct
{
    void Update(T1 item1);
}

public interface ISystem<T1, T2>
    where T1 : allows ref struct
    where T2 : allows ref struct
{
    void Update(T1 item1, T2 item2);
}

public interface ISystem<T1, T2, T3>
    where T1 : allows ref struct
    where T2 : allows ref struct
    where T3 : allows ref struct
{
    void Update(T1 item1, T2 item2, T3 item3);
}

public interface ISystem<T1, T2, T3, T4>
    where T1 : allows ref struct
    where T2 : allows ref struct
    where T3 : allows ref struct
    where T4 : allows ref struct
{
    void Update(T1 item1, T2 item2, T3 item3, T4 item4);
}

public interface ISystem<T1, T2, T3, T4, T5>
    where T1 : allows ref struct
    where T2 : allows ref struct
    where T3 : allows ref struct
    where T4 : allows ref struct
    where T5 : allows ref struct
{
    void Update(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5);
}

public interface ISystem<T1, T2, T3, T4, T5, T6>
    where T1 : allows ref struct
    where T2 : allows ref struct
    where T3 : allows ref struct
    where T4 : allows ref struct
    where T5 : allows ref struct
    where T6 : allows ref struct
{
    void Update(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6);
}

public interface ISystem<T1, T2, T3, T4, T5, T6, T7>
    where T1 : allows ref struct
    where T2 : allows ref struct
    where T3 : allows ref struct
    where T4 : allows ref struct
    where T5 : allows ref struct
    where T6 : allows ref struct
    where T7 : allows ref struct
{
    void Update(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7);
}

public interface ISystem<T1, T2, T3, T4, T5, T6, T7, T8>
    where T1 : allows ref struct
    where T2 : allows ref struct
    where T3 : allows ref struct
    where T4 : allows ref struct
    where T5 : allows ref struct
    where T6 : allows ref struct
    where T7 : allows ref struct
    where T8 : allows ref struct
{
    void Update(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7, T8 item8);
}
