namespace Pixely.Models;

/// <summary>Which way a draw's front faces wind on screen, so it can pick <c>FrontFace</c> and <c>CullMode</c>.</summary>
public enum Winding
{
    /// <summary>Front faces wind counter-clockwise, as glTF writes them: <c>FrontFace.CounterClockwise</c> with back-face culling.</summary>
    CounterClockwise,

    /// <summary>The transform mirrors the mesh, so front faces wind clockwise: <c>FrontFace.Clockwise</c> with back-face culling.</summary>
    Clockwise,

    /// <summary>Triangles wind both ways, or the transform collapses them so they have no winding: draw with <c>CullMode.None</c>.</summary>
    Mixed
}
