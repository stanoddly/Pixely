using System.Numerics;

namespace Pixely.Ui.Tests;

/// <summary>
/// A scroll target that records what it was offered and takes whatever it is told to, so routing
/// can be tested apart from what a real scroll view does with a delta.
/// </summary>
internal sealed class RecordingScrollTarget : Element, IScrollTarget
{
    public List<string> Calls { get; } = new();

    /// <summary>The axes this target reports having taken.</summary>
    public ScrollAxes Takes { get; set; } = ScrollAxes.Both;

    /// <summary>Runs inside the callback, which is where a target gets to route the pointer again.</summary>
    public Action? WhenScrolled { get; set; }

    ScrollAxes IScrollTarget.OnScroll(Vector2Int position, Vector2 delta)
    {
        Calls.Add($"scroll {position.X},{position.Y} {delta.X},{delta.Y}");
        WhenScrolled?.Invoke();
        return Takes;
    }
}
