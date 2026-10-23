namespace Pixely.Ecs;

// Marks a component without data. A tag has no column, so adding or removing it moves no value; a query filters on it with [With<T>] or [Without<T>].
public interface ITag;
