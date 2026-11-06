namespace Orangey.Vocabulary;

public enum Facing
{
    North,
    South
}

public readonly record struct ItemId(int Value);

public record struct MutableCoordinate(int X, int Y);

public sealed class Helper;

internal enum Hidden
{
    None
}

public static class Limits
{
    public const int MaxStack = 99;

    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(1);
}

public static class Counters
{
    public const int Start = 0;

    public static int Next;
}

public static class Formulas
{
    public const int Base = 10;

    public static int Scale(int value) => value * Base;
}

public static class Thresholds
{
    public static readonly int[] Values = [1, 2];
}
