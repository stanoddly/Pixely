namespace Pixely.Ecs.Tests;

public record struct Position(float X, float Y);

public record struct Velocity(float X, float Y);

public record struct Name(string Value);

public struct Frozen;

public struct Player;

[Query]
public ref partial struct Movement
{
    public ReadOnlySpan<Entity> Entities;
    public Span<Position> Positions;
    public ReadOnlySpan<Velocity> Velocities;
}

[Query]
[Without<Frozen>]
public ref partial struct UnfrozenPositions
{
    public ReadOnlySpan<Entity> Entities;
    public ReadOnlySpan<Position> Positions;
}

[Query]
[With<Player>]
public readonly ref partial struct PlayerEntities
{
    public readonly ReadOnlySpan<Entity> Entities;
}

public partial class Container
{
    [Query]
    internal ref partial struct NestedNames
    {
        public ReadOnlySpan<Name> Names;
    }
}

// Hand-written, because the generator rejects a query that requires no component.
public ref struct EverythingQuery : IQuery<EverythingQuery>
{
    public static void Describe(QueryDescription description)
    {
    }

    public static EverythingQuery Create(ArchetypeChunk chunk) => new EverythingQuery();
}

public ref struct ManualPositions : IQuery<ManualPositions>
{
    public ReadOnlySpan<Entity> Entities;
    public Span<Position> Positions;
    public bool HasVelocity;

    public static void Describe(QueryDescription description) => description.With<Position>();

    public static ManualPositions Create(ArchetypeChunk chunk) =>
        new ManualPositions { Entities = chunk.Entities, Positions = chunk.GetSpan<Position>(), HasVelocity = chunk.Has<Velocity>() };
}

// Wrong on purpose: it reads Velocity without requiring it.
public ref struct VelocitiesOfPositions : IQuery<VelocitiesOfPositions>
{
    public Span<Velocity> Velocities;

    public static void Describe(QueryDescription description) => description.With<Position>();

    public static VelocitiesOfPositions Create(ArchetypeChunk chunk) => new VelocitiesOfPositions { Velocities = chunk.GetSpan<Velocity>() };
}

public struct Dead;

[Query]
[With<Dead>]
public ref partial struct Corpses
{
    public ReadOnlySpan<Entity> Entities;
}

[Blueprint]
public partial struct MovingBlueprint
{
    public Position Position;
    public Velocity Velocity;
}

[Blueprint]
public readonly partial record struct NamedBlueprint(Name Name, Position Position);
