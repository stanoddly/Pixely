namespace Pixely.Ecs;

public readonly struct Entity : IEquatable<Entity>
{
    internal Entity(int index, int version)
    {
        Index = index;
        Version = version;
    }

    internal int Index { get; }

    // Starts at 1, so default(Entity) is never alive.
    internal int Version { get; }

    public bool Equals(Entity other) => Index == other.Index && Version == other.Version;

    public override bool Equals(object? obj) => obj is Entity other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Index, Version);

    public override string ToString() => $"Entity {{ Index={Index}, Version={Version} }}";

    public static bool operator ==(Entity left, Entity right) => left.Equals(right);

    public static bool operator !=(Entity left, Entity right) => !left.Equals(right);
}
