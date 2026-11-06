namespace Pixely.Ecs.Tests;

public record struct Position(float X, float Y);

public record struct Velocity(float X, float Y);

public record struct Name(string Value);

public struct Frozen : ITag;

public struct Player;

public sealed class Movement : Query<ReadOnlySpan<Entity>, Span<Position>, ReadOnlySpan<Velocity>>;

// Movement without writing, so code outside systems can take it through AddQuery.
public sealed class MovingEntities : Query<ReadOnlySpan<Entity>, ReadOnlySpan<Position>, ReadOnlySpan<Velocity>>;

public sealed class UnfrozenPositions : Query<ReadOnlySpan<Entity>, ReadOnlySpan<Position>>, IWithout<Frozen>;

// Moves the entities that have a Velocity and visits those that lack it too.
public sealed class OptionalMovement : Query<ReadOnlySpan<Entity>, Span<Position>, Optional<ReadOnlySpan<Velocity>>>;

// Wrong on purpose: IWith<...> or IWithout<...> names the optional component. No system names them, so only the world checks them.
public sealed class OptionalRequiredVelocities : Query<ReadOnlySpan<Position>, Optional<ReadOnlySpan<Velocity>>>, IWith<Velocity>;

public sealed class OptionalExcludedVelocities : Query<ReadOnlySpan<Position>, Optional<ReadOnlySpan<Velocity>>>, IWithout<Velocity>;

public sealed class PlayerEntities : Query<ReadOnlySpan<Entity>>, IWith<Player>;

// Wrong on purpose: it requires no component.
public sealed class EntitiesOnly : Query<ReadOnlySpan<Entity>>;

public struct Dead : ITag;

public sealed class Corpses : Query<ReadOnlySpan<Entity>>, IWith<Dead>;

public sealed class MovingSpawner : Spawner<Position, Velocity>;

// Spawns the components of MovingSpawner, so both spawn into one archetype.
public sealed class SecondMovingSpawner : Spawner<Position, Velocity>;

public sealed class FrozenSpawner : Spawner<Position>, IWith<Frozen>;

public sealed class Positions : Query<ReadOnlySpan<Position>>;

// Wrong on purpose: it requires and excludes Position. No system names it, so only the world checks it.
public sealed class ExcludedPositions : Query<ReadOnlySpan<Position>>, IWithout<Position>;

public record struct Mass(float Value);

// Queries whose Query<...> comes through a generic base, so the generator registers their spans through the derived class or the closed type.
public abstract class ComponentQuery<T> : Query<ReadOnlySpan<T>> where T : struct;

public sealed class Masses : ComponentQuery<Mass>;

public sealed class Writable<T> : Query<Span<T>> where T : struct;
