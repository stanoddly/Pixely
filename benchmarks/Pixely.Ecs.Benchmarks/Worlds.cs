using ArchEntity = Arch.Core.Entity;
using ArchWorld = Arch.Core.World;

namespace Pixely.Ecs.Benchmarks;

// Both worlds hold the same entities: a third each in {Position, Velocity}, {Position, Velocity, Player} and {Position, Velocity, Enemy}.
public static class Worlds
{
    public static (World World, Entity[] Entities) CreatePixely(int count)
    {
        World world = new World();
        Entity[] entities = new Entity[count];
        for (int i = 0; i < count; i++)
        {
            Entity entity = world.Create();
            world.Add(entity, new Position(i, i));
            world.Add(entity, new Velocity(1, 1));
            switch (i % 3)
            {
                case 1:
                    world.Add(entity, new Player());
                    break;
                case 2:
                    world.Add(entity, new Enemy());
                    break;
            }

            entities[i] = entity;
        }

        return (world, entities);
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
