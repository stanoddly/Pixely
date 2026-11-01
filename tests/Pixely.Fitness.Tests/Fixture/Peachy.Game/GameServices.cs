using Peachy.Game.State;
using Pixely.DependencyInjection;

namespace Peachy.Game;

public static class GameServices
{
    public static void AddGame(this ServiceCollection services)
    {
        services.AddSingleton<World>();
    }
}
