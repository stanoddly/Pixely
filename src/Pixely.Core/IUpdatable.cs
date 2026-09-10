namespace Pixely;

public interface IUpdatable
{
    /// <summary>
    /// The order the update runs in relative to the other updatables. Lower numbers run first.
    /// </summary>
    int UpdateOrder => 0;

    void Update();
}
