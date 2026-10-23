namespace Pixely.Ecs.Benchmarks;

public record struct Position(float X, float Y);

public record struct Velocity(float X, float Y);

public struct Player;

public struct Enemy;

public struct Frozen;

[Query]
public ref partial struct Movement
{
    public Span<Position> Positions;
    public ReadOnlySpan<Velocity> Velocities;
}

[Blueprint]
public partial struct MovingBlueprint
{
    public Position Position;
    public Velocity Velocity;
}
