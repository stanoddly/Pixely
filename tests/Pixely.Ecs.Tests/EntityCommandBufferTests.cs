namespace Pixely.Ecs.Tests;

public class EntityCommandBufferTests
{
    [Test]
    public void ChangesRecordedDuringQueryApplyAfterIt()
    {
        World world = new World();
        Entity stopped = CreateMoving(world, new Velocity(0, 0));
        Entity moving = CreateMoving(world, new Velocity(1, 0));
        EntityCommandBuffer commands = new EntityCommandBuffer();

        foreach (Movement chunk in world.Query<Movement>())
        {
            for (int i = 0; i < chunk.Count; i++)
            {
                if (chunk.Velocities[i] == default)
                {
                    commands.Destroy(chunk.Entities[i]);
                }
                else
                {
                    commands.Add(chunk.Entities[i], new Name("moving"));
                    commands.Remove<Velocity>(chunk.Entities[i]);
                }
            }
        }

        Assert.That(world.IsAlive(stopped), Is.True);

        commands.Apply(world);

        Assert.That(world.IsAlive(stopped), Is.False);
        Assert.That(world.Get<Name>(moving), Is.EqualTo(new Name("moving")));
        Assert.That(world.Has<Velocity>(moving), Is.False);
    }

    [Test]
    public void ApplyClearsTheBuffer()
    {
        World world = new World();
        Entity entity = world.Create();
        EntityCommandBuffer commands = new EntityCommandBuffer();
        commands.Add(entity, new Position(1, 1));

        commands.Apply(world);
        commands.Apply(world);

        Assert.That(world.Get<Position>(entity), Is.EqualTo(new Position(1, 1)));
    }

    [Test]
    public void ApplyClearsTheBufferWhenACommandThrows()
    {
        World world = new World();
        Entity entity = world.Create();
        EntityCommandBuffer commands = new EntityCommandBuffer();
        commands.Destroy(entity);
        commands.Destroy(entity);

        Assert.Throws<ArgumentException>(() => commands.Apply(world));
        Assert.DoesNotThrow(() => commands.Apply(world));
    }

    [Test]
    public void ApplyThrowsWhileQueryRuns()
    {
        World world = new World();
        Entity entity = CreateMoving(world, new Velocity());
        EntityCommandBuffer commands = new EntityCommandBuffer();
        commands.Add(entity, new Frozen());

        foreach (Movement _ in world.Query<Movement>())
        {
            Assert.Throws<InvalidOperationException>(() => commands.Apply(world));
        }
    }

    private static Entity CreateMoving(World world, Velocity velocity)
    {
        Entity entity = world.Create();
        world.Add(entity, new Position());
        world.Add(entity, velocity);
        return entity;
    }
}
