namespace Pixely.Ecs.Benchmarks;

public record struct Position(float X, float Y);

public record struct Velocity(float X, float Y);

public struct Player : ITag;

public struct Enemy : ITag;

public struct Frozen : ITag;

public sealed class Movement : Query<Span<Position>, ReadOnlySpan<Velocity>>;

[Blueprint]
public partial struct MovingBlueprint
{
    public Position Position;
    public Velocity Velocity;
}
