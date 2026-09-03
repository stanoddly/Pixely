namespace Pixely.PathFinding.Grids;

/// <summary>
/// Selects which neighbours a grid step may reach.
/// </summary>
public enum GridConnectivity
{
    /// <summary>Steps to the four cardinal neighbours.</summary>
    FourWay,

    /// <summary>Steps to the four cardinal and four diagonal neighbours.</summary>
    EightWay,

    /// <summary>Steps to the four cardinal and four diagonal neighbours, where a diagonal step also requires both of its cardinal steps.</summary>
    EightWayNoCornerCutting
}
