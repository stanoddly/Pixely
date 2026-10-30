using ArchEntity = Arch.Core.Entity;
using ArchWorld = Arch.Core.World;

namespace Pixely.Ecs.Benchmarks;

// Both worlds hold the same entities: a third each in {Position, Velocity}, {Position, Velocity, Player} and {Position, Velocity, Enemy}.
public static class Worlds
{
    internal sealed class PopulatingSystem(MovingSpawner moving, PlayerSpawner players, EnemySpawner enemies, Entity[] entities) : ISystem
    {
        public void Update()
        {
            for (int i = 0; i < entities.Length; i++)
            {
                Position position = new Position(i, i);
                Velocity velocity = new Velocity(1, 1);
                entities[i] = (i % 3) switch
                {
                    1 => players.Spawn(position, velocity),
                    2 => enemies.Spawn(position, velocity),
                    _ => moving.Spawn(position, velocity)
                };
            }
        }
    }

    // Spawns the entities through a system, as a game does, with a scheduler of its own.
    public static Entity[] PopulatePixely(World world, int count)
    {
        Entity[] entities = new Entity[count];
        SystemScheduler scheduler = new SystemScheduler(world);
        scheduler.Add(builder => new PopulatingSystem(builder.Spawner<MovingSpawner>(), builder.Spawner<PlayerSpawner>(), builder.Spawner<EnemySpawner>(), entities));
        scheduler.Update();
        return entities;
    }

    public static (ArchWorld World, ArchEntity[] Entities) CreateArch(int count)
    {
        ArchWorld world = ArchWorld.Create();
        ArchEntity[] entities = new ArchEntity[count];
        for (int i = 0; i < count; i++)
        {
            entities[i] = (i % 3) switch
            {
                1 => world.Create(new Position(i, i), new Velocity(1, 1), new Player()),
                2 => world.Create(new Position(i, i), new Velocity(1, 1), new Enemy()),
                _ => world.Create(new Position(i, i), new Velocity(1, 1))
            };
        }

        return (world, entities);
    }
}
