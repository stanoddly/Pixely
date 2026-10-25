namespace Pixely.Ecs.Tests;

public record struct Position(float X, float Y);

public record struct Velocity(float X, float Y);

public record struct Name(string Value);

public struct Frozen : ITag;

public struct Player;

public sealed class Movement : Query<ReadOnlySpan<Entity>, Span<Position>, ReadOnlySpan<Velocity>>;

public sealed class UnfrozenPositions : Query<ReadOnlySpan<Entity>, ReadOnlySpan<Position>>, IWithout<Frozen>;

public sealed class PlayerEntities : Query<ReadOnlySpan<Entity>>, IWith<Player>;

// Wrong on purpose: it requires no component.
public sealed class EntitiesOnly : Query<ReadOnlySpan<Entity>>;

public struct Dead : ITag;

public sealed class Corpses : Query<ReadOnlySpan<Entity>>, IWith<Dead>;

[Blueprint]
public partial struct MovingBlueprint
{
    public Position Position;
    public Velocity Velocity;
}

// Describes the components of MovingBlueprint, so both spawn into one archetype.
[Blueprint]
public partial struct SecondMovingBlueprint
{
    public Position Position;
    public Velocity Velocity;
}

[Blueprint]
public readonly partial record struct NamedBlueprint(Name Name, Position Position);

[Blueprint]
public partial struct FrozenBlueprint
{
    public Position Position;
    public Frozen Frozen;
}

[Blueprint]
public partial struct MarkerBlueprint
{
    public Frozen Frozen;
}

public sealed class Positions : Query<ReadOnlySpan<Position>>;

// Wrong on purpose: it requires and excludes Position. No system names it, so only the world checks it.
public sealed class ExcludedPositions : Query<ReadOnlySpan<Position>>, IWithout<Position>;

public record struct Mass(float Value);

// Queries whose Query<...> comes through a generic base, so the generator registers their spans through the derived class or the closed type.
public abstract class ComponentQuery<T> : Query<ReadOnlySpan<T>> where T : struct;

public sealed class Masses : ComponentQuery<Mass>;

public sealed class Writable<T> : Query<Span<T>> where T : struct;
