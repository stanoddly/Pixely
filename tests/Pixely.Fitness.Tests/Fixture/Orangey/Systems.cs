// The fixtures' constructor parameters exist only for the rules to reflect over.
#pragma warning disable CS9113

using Orangey.Components;
using Pixely.Ecs;

namespace Orangey.Components
{
    public record struct Position(int Value);

    public record struct Velocity(int Value);

    public record struct Fuel(int Value);

    public record struct Heat(int Value);

    public struct Player : ITag;

    public struct Spark : ITag;

    public struct Wet : ITag;

    public record struct Thrust(int Value);

    public record struct Mass(int Value);

    public record struct Moisture(int Value);

    public record struct Ore(int Value);

    public interface IReadOnlyGround;

    public sealed class Ground : IReadOnlyGround;

    public class Climate;

    public sealed class Storm : Climate;
}

namespace Orangey.WorldAccess
{
    public sealed class Movers : Query<Span<Position>, ReadOnlySpan<Velocity>>;

    public sealed class Positions : Query<ReadOnlySpan<Position>>;

    public sealed class PlayerTanks : Query<Span<Fuel>>, IWith<Player>;

    public sealed class EnemyTanks : Query<Span<Fuel>>, IWithout<Player>;

    public sealed class Sparks : Spawner<Heat>, IWith<Spark>;

    public sealed class Coolable : Query<Span<Heat>>, IWithout<Spark>;

    public sealed class Hot : Query<ReadOnlySpan<Heat>>, IWith<Spark>;

    public sealed class ThrustInputs : Query<Span<Thrust>>;

    public sealed class Thrusters : Query<ReadOnlySpan<Thrust>>;

    public sealed class Weighed : Query<Span<Mass>>, IWith<Position>;

    public sealed class Dryable : Query<Span<Moisture>>, IWithout<Wet>;

    public sealed class Asteroids : Spawner<Ore>;

    public sealed class Cargo : Query<ReadOnlySpan<Ore>>, IWith<Player>;
}

namespace Orangey.Systems
{
    using Orangey.WorldAccess;

    public sealed class Movement(Movers movers) : ISystem
    {
        public void Update()
        {
        }
    }

    // Reads what Movement writes, with no declared order.
    public sealed class Minimap(Positions positions) : ISystem
    {
        public void Update()
        {
        }
    }

    public sealed class Camera(Positions positions) : ISystem, IAfter<Movement>
    {
        public void Update()
        {
        }
    }

    // Ordered after Movement through Camera.
    public sealed class Radar(Positions positions) : ISystem, IAfter<Camera>
    {
        public void Update()
        {
        }
    }

    // Ordered before Movement from the other side.
    public sealed class Trail(ComponentLookup<Position> positions) : ISystem, IBefore<Movement, Minimap>
    {
        public void Update()
        {
        }
    }

    public sealed class PlayerFuel(PlayerTanks tanks) : ISystem
    {
        public void Update()
        {
        }
    }

    // Reaches no entity PlayerFuel reaches.
    public sealed class EnemyFuel(EnemyTanks tanks) : ISystem
    {
        public void Update()
        {
        }
    }

    // An adder reaches any entity, so it meets both fuel systems.
    public sealed class Refueling(ComponentAdder<Fuel> fuel) : ISystem, IAfter<PlayerFuel>
    {
        public void Update()
        {
        }
    }

    // Spawns only entities with Spark, which Cooling skips and Glowing reads.
    public sealed class Emitter(Sparks sparks) : ISystem
    {
        public void Update()
        {
        }
    }

    public sealed class Cooling(Coolable coolable) : ISystem
    {
        public void Update()
        {
        }
    }

    public sealed class Glowing(Hot hot) : ISystem
    {
        public void Update()
        {
        }
    }

    public sealed class Terraform(Unique<Ground> ground) : ISystem
    {
        public void Update()
        {
        }
    }

    public sealed class Erosion(ReadOnlyUnique<IReadOnlyGround> ground) : ISystem
    {
        public void Update()
        {
        }
    }

    public sealed class Survey(ReadOnlyUnique<IReadOnlyGround> ground) : ISystem, IBefore<Terraform>
    {
        public void Update()
        {
        }
    }

    public abstract class InputSystem : ISystem
    {
        public abstract void Update();
    }

    public sealed class Keyboard(ThrustInputs inputs) : InputSystem
    {
        public override void Update()
        {
        }
    }

    // Ordered after Keyboard through its base class, as the scheduler orders it.
    public sealed class Engines(Thrusters thrusters) : ISystem, IAfter<InputSystem>
    {
        public void Update()
        {
        }
    }

    // Filters on the Position that Movement and Trail write, which does not change which entities it reaches.
    public sealed class Weighing(Weighed weighed) : ISystem
    {
        public void Update()
        {
        }
    }

    public sealed class Drying(Dryable dryable) : ISystem
    {
        public void Update()
        {
        }
    }

    // Adding Wet changes which entities Drying reaches.
    public sealed class Soaking(ComponentAdder<Wet> wet) : ISystem
    {
        public void Update()
        {
        }
    }

    // Spawns entities without Player, which Trading requires.
    public sealed class Mining(Asteroids asteroids) : ISystem
    {
        public void Update()
        {
        }
    }

    public sealed class Trading(Cargo cargo) : ISystem
    {
        public void Update()
        {
        }
    }

    // Writes another unique than Lightning, because the world keys uniques by their exact class.
    public sealed class Weather(Unique<Climate> climate) : ISystem
    {
        public void Update()
        {
        }
    }

    public sealed class Lightning(Unique<Storm> storm) : ISystem
    {
        public void Update()
        {
        }
    }

    // Climate does not implement Storm, so Weather writes no unique this reads.
    public sealed class Forecast(ReadOnlyUnique<Storm> storm) : ISystem, IAfter<Lightning>
    {
        public void Update()
        {
        }
    }
}

namespace Orangey.Systems.Flight
{
    public sealed class Takeoff : ISystem
    {
        public void Update()
        {
        }
    }
}

namespace Orangey.Vocabulary.Units
{
    public readonly record struct Meters(int Value);

    public sealed class Converter;
}

namespace Orangey.Tools
{
    public static class Cheats;
}

namespace Orangey.Misc
{
    public static class Leftovers;
}

namespace Orangey.Systems2
{
    public static class NotASubNamespace;
}
