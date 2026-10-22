namespace Pixely.Ecs;

// The [Query] source generator implements this; a hand-written query implements it directly.
public interface IQuery<TSelf> where TSelf : IQuery<TSelf>, allows ref struct
{
    static abstract void Describe(QueryDescription description);

    static abstract TSelf Create(Archetype archetype);
}

[AttributeUsage(AttributeTargets.Struct)]
public sealed class QueryAttribute : Attribute;

[AttributeUsage(AttributeTargets.Struct, AllowMultiple = true)]
public sealed class WithAttribute<T> : Attribute where T : struct;

[AttributeUsage(AttributeTargets.Struct, AllowMultiple = true)]
public sealed class WithoutAttribute<T> : Attribute where T : struct;
