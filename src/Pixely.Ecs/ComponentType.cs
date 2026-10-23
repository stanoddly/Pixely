namespace Pixely.Ecs;

// Ids are process-wide, so archetypes of different worlds agree on them.
internal sealed class ComponentTypes : StaticTypeIdMap<ComponentTypes>;

internal sealed class ComponentType<T> : StaticTypeIdMap<ComponentTypes, T> where T : struct;
