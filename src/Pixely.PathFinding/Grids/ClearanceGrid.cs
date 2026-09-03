namespace Pixely.PathFinding.Grids;

/// <summary>
/// Stores the largest free square anchored at each tile, so testing whether a square agent fits at an anchor is one array read.
/// </summary>
/// <remarks>
/// The anchor is the minimum corner: the clearance at <c>(x, y)</c> measures the free square extending toward increasing x and y.
/// An agent of size N anchored at <c>(x, y)</c> therefore occupies the tiles from <c>(x, y)</c> through <c>(x + N - 1, y + N - 1)</c>,
/// which places its centre half a footprint away from its anchor.
/// <para>
/// Clearance answers the static question only, and one grid serves every agent size at once: <see cref="Fits"/> takes the size as an argument
/// rather than the grid being built for one. Per-query exceptions stay outside it. A consumer that adds a temporary blocker layers that
/// exception over the clearance result through an <see cref="IGridOverlay"/>; a consumer that wants to ignore an existing blocker rebuilds a
/// scratch grid from amended flags, because layering can only make a tile less walkable than its clearance says, never more.
/// </para>
/// <para>The grid owns one byte per tile and <see cref="Rebuild"/> reuses that buffer, so rebuilding at whatever cadence the topology changes does not allocate.</para>
/// </remarks>
public sealed class ClearanceGrid
{
    /// <summary>The largest agent size <see cref="Fits"/> can answer for. Clearances saturate here.</summary>
    public const int MaximumAgentSize = byte.MaxValue;

    private readonly byte[] _clearances;

    public ClearanceGrid(GridGeometry geometry)
    {
        Geometry = geometry;
        _clearances = new byte[geometry.NodeCount];
    }

    public GridGeometry Geometry { get; }

    /// <summary>The clearance of every tile in row-major order.</summary>
    public ReadOnlySpan<byte> Clearances => _clearances;

    /// <summary>
    /// Recomputes every clearance from the blocked flags of all tiles in row-major order, in one pass over the grid.
    /// </summary>
    public void Rebuild(ReadOnlySpan<bool> blocked)
    {
        if (blocked.Length < _clearances.Length)
        {
            throw new ArgumentException("The blocked buffer must contain an entry for every grid tile.", nameof(blocked));
        }

        int width = Geometry.Width;
        int height = Geometry.Height;
        for (int y = height - 1; y >= 0; y--)
        {
            int rowOffset = y * width;
            bool hasRowBelow = y + 1 < height;
            for (int x = width - 1; x >= 0; x--)
            {
                int index = rowOffset + x;
                if (blocked[index])
                {
                    _clearances[index] = 0;
                    continue;
                }

                bool hasColumnAfter = x + 1 < width;
                int right = hasColumnAfter ? _clearances[index + 1] : 0;
                int below = hasRowBelow ? _clearances[index + width] : 0;
                int diagonal = hasColumnAfter && hasRowBelow ? _clearances[index + width + 1] : 0;
                int clearance = 1 + Math.Min(right, Math.Min(below, diagonal));
                _clearances[index] = (byte)Math.Min(clearance, MaximumAgentSize);
            }
        }
    }

    /// <summary>
    /// Returns the side length of the largest free square anchored at the tile.
    /// </summary>
    public int GetClearance(int index)
    {
        return _clearances[index];
    }

    /// <summary>
    /// Returns whether a square agent of the given size fits when anchored at the tile.
    /// </summary>
    /// <param name="index">The anchor tile, which is the minimum corner of the agent's footprint.</param>
    /// <param name="agentSize">The agent's side length, from one through <see cref="MaximumAgentSize"/>.</param>
    public bool Fits(int index, int agentSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(agentSize, 1, nameof(agentSize));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(agentSize, MaximumAgentSize, nameof(agentSize));
        return _clearances[index] >= agentSize;
    }
}
