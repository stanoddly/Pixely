using System.Numerics;
using Pixely;

namespace Peachy.Game.State;

public sealed class World
{
    public Chore CurrentChore = new IdleChore();
    public Vector2 Position;
    public Rectangle Bounds;
    private readonly Vector2[] path = [];

    public ReadOnlySpan<Vector2> Path => path;

    public Rectangle Area => Bounds;
}
