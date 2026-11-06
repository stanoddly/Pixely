namespace Pixely.Ecs;

// Marks a component without data. A tag has no column, so adding or removing it moves no value; a query filters on it with IWith<T> or IWithout<T>.
public interface ITag;
