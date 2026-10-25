namespace Pixely.Ecs.Benchmarks;

public record struct Position(float X, float Y);

public record struct Velocity(float X, float Y);

public struct Player : ITag;

public struct Enemy : ITag;

public struct Frozen : ITag;

public record struct Health(float Value);

public sealed class Movement : Query<Span<Position>, ReadOnlySpan<Velocity>>;

public sealed class MovingSpawner : Spawner<Position, Velocity>;
