namespace Pixely.Ecs;

// Updated by a SystemScheduler once per update. The queries, lookups and writers its constructor takes are its contract; the rest of the constructor
// takes services.
public interface ISystem
{
    void Update();
}
