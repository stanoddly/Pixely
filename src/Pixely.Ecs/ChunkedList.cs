using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pixely.Ecs;

// Grows by adding fixed-size chunks instead of copying into a larger array, so a long list neither copies nor reaches the large object heap.
// The indexer skips bounds checks: callers pass an index below Count.
internal sealed class ChunkedList<T>
{
    private static readonly int Shift = BitOperations.Log2((uint)Math.Max(16, 16 * 1024 / Unsafe.SizeOf<T>()));
    private static readonly int ChunkLength = 1 << Shift;

    private T[][] _chunks = new T[4][];
    private int _chunkCount;

    public int Count { get; private set; }

    public ref T this[int index]
    {
        get
        {
            Debug.Assert((uint)index < (uint)Count);
            T[] chunk = Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_chunks), index >> Shift);
            return ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(chunk), index & (ChunkLength - 1));
        }
    }

    public int Add(T value)
    {
        int chunk = Count >> Shift;
        if (chunk == _chunkCount)
        {
            if (_chunkCount == _chunks.Length)
            {
                Array.Resize(ref _chunks, _chunks.Length * 2);
            }

            _chunks[_chunkCount++] = new T[ChunkLength];
        }

        _chunks[chunk][Count & (ChunkLength - 1)] = value;
        return Count++;
    }

    // The items from index on, up to count of them, that lie in index's chunk.
    public ReadOnlySpan<T> GetRun(int index, int count)
    {
        int offset = index & (ChunkLength - 1);
        return _chunks[index >> Shift].AsSpan(offset, Math.Min(count, ChunkLength - offset));
    }

    // Keeps the chunks for reuse and clears the items only when they hold references.
    public void Clear()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            for (int i = 0; i < Count; i += ChunkLength)
            {
                _chunks[i >> Shift].AsSpan(0, Math.Min(ChunkLength, Count - i)).Clear();
            }
        }

        Count = 0;
    }
}
