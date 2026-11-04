namespace Pixely.Ecs;

// An index and a version, 8 bytes, without a world id: one made Entity 12 bytes and cost 2 to 8% on creation and structural changes. So an entity of
// another world is not detected, and when this world has an entity with the same index and version, a call acts on that entity.
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
