namespace Pixely.Ecs;

public sealed class QueryDescription
{
    private readonly List<int> _required = [];
    private readonly List<int> _excluded = [];

    internal QueryDescription()
    {
    }

    public void With<T>() where T : struct => _required.Add(ComponentType<T>.Id);

    public void Without<T>() where T : struct => _excluded.Add(ComponentType<T>.Id);

    internal ComponentSet Required => ComponentSet.From(_required);

    internal ComponentSet Excluded => ComponentSet.From(_excluded);
}
