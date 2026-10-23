namespace Pixely.Ecs;

// Updated by a SystemScheduler. Its constructor declares the queries it reads and the structural changes it makes.
public interface ISystem
{
    void Update();
}
