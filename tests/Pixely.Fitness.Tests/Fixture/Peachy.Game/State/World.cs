namespace Peachy.Game.State;

public sealed class World
{
    public Chore CurrentChore = new IdleChore();
}
